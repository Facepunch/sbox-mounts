await Sandbox.Mounting.Directory.Mount( "payday" );
await Sandbox.Mounting.Directory.Mount( "hl1" );
var results = new System.Collections.Generic.List<string>();
foreach ( var kind in new[] { "taser", "cloaker" } )
foreach ( var reason in new[] { "timeout", "death", "disabled", "destroyed", "wall", "transfer", "victim-death" } )
{
	var path = $"mount://payday/entities/npcs/{kind}.prefab";
	var opponent = "mount://hl1/entities/npcs/monster_human_grunt.prefab";
	await ResourceLibrary.LoadAsync<PrefabFile>( path );
	await ResourceLibrary.LoadAsync<PrefabFile>( opponent );
	var scene = new Scene();
	try
	{
		using var scope = scene.Push();
		scene.Enabled = true;
		var floor = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
		floor.Scale = new Vector3( 2000, 2000, 20 ); floor.Center = Vector3.Down * 10;
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		async Task Tick( int count ) { for ( int i = 0; i < count; i++ ) { scene.GameTick( .05 ); await Task.Delay( 10 ); } }
		var attacker = GameObject.GetPrefab( path ).Clone( Vector3.Zero, Rotation.Identity ).GetComponent<Sandbox.Npcs.BaseCombatNpc>();
		var victim = GameObject.GetPrefab( opponent ).Clone( new Vector3( 85, 0, 0 ), Rotation.Identity ).GetComponent<Sandbox.Npcs.BaseNpc>();
		victim.SetRelationship( attacker.GameObject, Sandbox.Npcs.Disposition.Neutral );
		var type = attacker.GetType();
		type.GetProperty( "HoldDuration" ).SetValue( attacker, reason == "timeout" ? .5f : 10f );
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
		if ( Phase() != "Holding" ) throw new Exception( $"{kind}/{reason}: no hold, {Phase()}, {attacker.WorldPosition}/{victim.WorldPosition}, schedule={attacker.ActiveSchedule}, health={victim.Health}." );
		GameObject other = null;
		switch ( reason )
		{
			case "death":
				((Component.IDamageable)attacker).OnDamage( new DamageInfo { Damage = 20000 } ); break;
			case "disabled": attacker.Enabled = false; break;
			case "destroyed": attacker.GameObject.Destroy(); break;
			case "wall":
				var wall = new GameObject( true, "Obstruction" ).AddComponent<BoxCollider>();
				wall.Scale = new Vector3( 4, 500, 200 );
				wall.WorldPosition = (attacker.WorldPosition + victim.WorldPosition) * .5f + Vector3.Up * 90;
				break;
			case "transfer":
				other = new GameObject( true, "Other carrier" );
				victim.StartCarry( other ); break;
			case "victim-death":
				((Component.IDamageable)victim).OnDamage( new DamageInfo { Damage = 20000 } ); break;
		}
		await Tick( 20 );
		if ( victim.IsValid() && victim.PhysicsCarrier != other )
			throw new Exception( $"{kind}/{reason}: leaked or stolen carrier." );
		if ( attacker.IsValid() && Phase() != "None" )
			throw new Exception( $"{kind}/{reason}: attack remained active." );
		results.Add( $"{kind}/{reason}: PASS" );
	}
	finally { scene.Destroy(); }
}
return results;
