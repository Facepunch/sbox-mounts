var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
await mount.RefreshInternal();
foreach ( var id in new[] { "security", "cop", "swat" } )
	await ResourceLibrary.LoadAsync<PrefabFile>( $"mount://payday/entities/npcs/{id}.prefab" );
await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/weapons/crosskill.prefab" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var floor = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
	floor.Scale = new Vector3( 3000, 3000, 20 ); floor.Center = new Vector3( 0, 0, -10 );
	await scene.NavMesh.Generate( scene.PhysicsWorld );
	void Tick( int n ) { for ( int i = 0; i < n; i++ ) scene.GameTick( .05 ); }
	IDisposable Clock() => Time.Scope( (double)typeof(Scene).GetProperty( "TimeNow", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance ).GetValue( scene ), .05 );
	var player = new GameObject( true, "Player" );
	player.WorldPosition = new Vector3( 300, 0, 0 );
	player.AddComponent<PlayerController>().UseInputControls = false;
	player.NetworkSpawn( true, Connection.Local );
	var inventory = player.AddComponent<BaseInventoryComponent>(); inventory.GiveOnStart = false;
	var bystander = new GameObject( true, "Innocent player" );
	bystander.WorldPosition = new Vector3( 300, 300, 0 );
	bystander.AddComponent<PlayerController>().UseInputControls = false;
	var results = new System.Collections.Generic.List<string>();
	foreach ( var id in new[] { "security", "cop" } )
	{
		var go = GameObject.GetPrefab( $"mount://payday/entities/npcs/{id}.prefab" ).Clone();
		var npc = go.GetComponent<Sandbox.Npcs.BaseCombatNpc>();
		Tick( 25 );
		if ( npc.GetDisposition( player ) != Sandbox.Npcs.Disposition.Neutral ) throw new Exception( $"{id} attacks unarmed player." );
		var wall = new GameObject( true, "Weapon visibility blocker" );
		wall.WorldPosition = new Vector3( 150, 0, 80 );
		wall.AddComponent<BoxCollider>().Scale = new Vector3( 20, 500, 160 );
		var gun = GameObject.GetPrefab( "mount://payday/entities/weapons/crosskill.prefab" ).Clone().GetComponent<BaseCombatWeapon>();
		inventory.Add( gun ); inventory.Switch( gun );
		Tick( 25 );
		if ( npc.GetDisposition( player ) != Sandbox.Npcs.Disposition.Neutral ) throw new Exception( $"{id} detected hidden gun." );
		wall.Destroy(); Tick( 10 );
		if ( npc.GetDisposition( player ) != Sandbox.Npcs.Disposition.Hostile ) throw new Exception( $"{id} ignored visible drawn gun." );
		if ( npc.GetDisposition( bystander ) != Sandbox.Npcs.Disposition.Neutral ) throw new Exception( $"{id} blamed innocent bystander." );
		inventory.Drop( gun ); gun.GameObject.Destroy();
		Tick( 5 );
		if ( npc.GetDisposition( player ) != Sandbox.Npcs.Disposition.Hostile ) throw new Exception( $"{id} forgot threat when gun was put away." );
		go.Destroy(); Tick( 1 );
		var attacked = GameObject.GetPrefab( $"mount://payday/entities/npcs/{id}.prefab" ).Clone();
		var victim = attacked.GetComponent<Sandbox.Npcs.BaseCombatNpc>();
		Tick( 2 );
		using ( Clock() ) ((Component.IDamageable)victim).OnDamage( new DamageInfo { Damage = 1, Attacker = player } );
		if ( victim.GetDisposition( player ) != Sandbox.Npcs.Disposition.Hostile ) throw new Exception( $"{id} ignored attack." );
		attacked.Destroy(); Tick( 1 );
		results.Add( $"{id}: unarmed neutrality, occluded gun, visible gun, innocent bystander, persistent hostility and damage escalation passed" );
	}
	var listener = GameObject.GetPrefab( "mount://payday/entities/npcs/security.prefab" ).Clone().GetComponent<Sandbox.Npcs.BaseCombatNpc>();
	Tick( 3 );
	using ( Clock() ) scene.GetSystem<Sandbox.Npcs.NpcSystem>().EmitStimulus( Sandbox.Npcs.StimulusKinds.Gunshot, player.WorldPosition, player, 1000, 2 );
	Tick( 10 );
	if ( listener.GetDisposition( player ) != Sandbox.Npcs.Disposition.Hostile ) throw new Exception( "Gunshot did not escalate." );
	var swat = GameObject.GetPrefab( "mount://payday/entities/npcs/swat.prefab" ).Clone().GetComponent<Sandbox.Npcs.BaseCombatNpc>();
	if ( swat.GetDisposition( player ) != Sandbox.Npcs.Disposition.Hostile ) throw new Exception( "SWAT lost combat-ready defaults." );
	results.Add( "Gunshot escalation and SWAT defaults passed" );
	return results;
}
finally { scene.Destroy(); }
