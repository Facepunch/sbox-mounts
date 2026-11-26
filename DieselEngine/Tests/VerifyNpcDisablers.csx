// Real foreign NPCs remain enabled/damageable; the shared carrier contract suspends their brain/mover.
try
{
foreach ( var id in new[] { "payday", "hl1", "hl2" } ) await Sandbox.Mounting.Directory.Mount( id );
var results = new System.Collections.Generic.List<object>();
foreach ( var kind in new[] { "taser", "cloaker" } )
foreach ( var opponent in new[] { "mount://hl1/entities/npcs/monster_human_grunt.prefab", "mount://hl2/entities/npcs/combine_soldier.prefab" } )
{
	var path = $"mount://payday/entities/npcs/{kind}.prefab";
	await ResourceLibrary.LoadAsync<PrefabFile>( path );
	await ResourceLibrary.LoadAsync<PrefabFile>( opponent );
	var scene = new Scene();
	try
	{
		using var scope = scene.Push();
		scene.Enabled = true;
		var floor = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
		floor.Scale = new Vector3( 8000, 8000, 20 ); floor.Center = Vector3.Down * 10;
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		async Task Tick( int count ) { for ( int i = 0; i < count; i++ ) { scene.GameTick( .05 ); await Task.Delay( 10 ); } }
		var attacker = GameObject.GetPrefab( path ).Clone( Vector3.Zero, Rotation.Identity ).GetComponent<Sandbox.Npcs.BaseCombatNpc>();
		var victim = GameObject.GetPrefab( opponent ).Clone( new Vector3( 220, 0, 0 ), Rotation.Identity ).GetComponent<Sandbox.Npcs.BaseNpc>();
		// Allow the approach; incoming fire deliberately interrupts a special attack.
		victim.SetRelationship( attacker.GameObject, Sandbox.Npcs.Disposition.Neutral );
		var type = attacker.GetType();
		type.GetProperty( "HoldDuration" ).SetValue( attacker, 10f );
		type.GetProperty( "UseRadio" ).SetValue( attacker, false );
		attacker.Health = attacker.MaxHealth = 10000;
		await Tick( 5 );
		victim.Health = victim.MaxHealth = 10000;
		string Phase() => type.GetProperty( "AttackPhase" ).GetValue( attacker ).ToString();
		for ( int i = 0; i < 400 && Phase() != "Holding"; i++ )
		{
			victim.Movement?.Stop();
			await Tick( 1 );
		}
		if ( Phase() != "Holding" )
			throw new Exception( $"{kind}/{opponent}: no special attack, phase={Phase()}, positions={attacker.WorldPosition}/{victim.WorldPosition}, grounded={attacker.IsOnGround}/{victim.IsOnGround}, schedule={attacker.ActiveSchedule}." );
		if ( victim.PhysicsCarrier != attacker.GameObject || !victim.Enabled || !victim.Active )
			throw new Exception( "Restraint did not retain an enabled victim with owned carry state." );
		victim.WorldRotation = Rotation.FromYaw( 180 );
		victim.ClearRelationship( attacker.GameObject );
		float before = attacker.Health, victimBefore = victim.Health;
		await Tick( 30 );
		if ( Phase() != "Holding" || victim.PhysicsCarrier != attacker.GameObject )
			throw new Exception( $"{kind}/{opponent}: restraint ended prematurely." );
		if ( attacker.Health != before ) throw new Exception( $"{kind}/{opponent}: victim attacked during hold check: phase={Phase()}, carrier={victim.PhysicsCarrier}, positions={attacker.WorldPosition}/{victim.WorldPosition}." );
		if ( victim.Health >= victimBefore ) throw new Exception( "Special attack did not damage the victim." );
		((Component.IDamageable)attacker).OnDamage( new DamageInfo { Damage = 1, Attacker = victim.GameObject } );
		if ( victim.PhysicsCarrier.IsValid() || Phase() != "None" ) throw new Exception( "Damage interruption did not immediately release victim." );
		float recoveryHealth = attacker.Health;
		for ( int i = 0; i < 400 && attacker.Health == recoveryHealth; i++ ) await Tick( 1 );
		if ( attacker.Health >= recoveryHealth ) throw new Exception( $"{kind}/{opponent}: victim failed to resume combat after release." );
		results.Add( new { Attacker = kind, Victim = opponent, Result = "PASS: acquisition, restraint, no victim attacks while held, ongoing damage, interruption and resumed foreign combat." } );
	}
	finally { scene.Destroy(); }
}
return results;
}
catch ( Exception e ) { throw new Exception( e.ToString() ); }
