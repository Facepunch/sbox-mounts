var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
await mount.RefreshInternal();
foreach ( var id in new[] { "civilian", "cop", "security" } )
	await ResourceLibrary.LoadAsync<PrefabFile>( $"mount://payday/entities/npcs/{id}.prefab" );
await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/weapons/crosskill.prefab" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var ground = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
	ground.Scale = new Vector3( 4000, 4000, 20 ); ground.Center = new Vector3( 0, 0, -10 );
	await scene.NavMesh.Generate( scene.PhysicsWorld );
	void Tick( int n ) { for ( int i = 0; i < n; i++ ) scene.GameTick( .05 ); }
	Sandbox.Npcs.BaseNpc Spawn( string id, Vector3 position )
	{
		var go = GameObject.GetPrefab( $"mount://payday/entities/npcs/{id}.prefab" ).Clone();
		go.WorldPosition = position;
		var npc = go.GetComponent<Sandbox.Npcs.BaseNpc>();
		npc.GetType().GetProperty( "Wander" )?.SetValue( npc, false );
		return npc;
	}
	bool Flag( Component c, string name ) => (bool)c.GetType().GetProperty( name ).GetValue( c );
	var player = new GameObject( true, "Player" );
	player.WorldPosition = new Vector3( 250, 0, 0 );
	player.AddComponent<PlayerController>().UseInputControls = false;
	player.NetworkSpawn( true, Connection.Local );
	var inventory = player.AddComponent<BaseInventoryComponent>(); inventory.GiveOnStart = false;
	var civilian = Spawn( "civilian", Vector3.Zero );
	Tick( 10 );
	if ( Flag( civilian, "Panicking" ) ) throw new Exception( "Civilian feared unarmed player." );
	var wall = new GameObject( true, "Sight blocker" );
	wall.WorldPosition = new Vector3( 125, 0, 80 );
	wall.AddComponent<BoxCollider>().Scale = new Vector3( 20, 500, 160 );
	var gun = GameObject.GetPrefab( "mount://payday/entities/weapons/crosskill.prefab" ).Clone().GetComponent<BaseCombatWeapon>();
	inventory.Add( gun ); inventory.Switch( gun );
	Tick( 15 );
	if ( Flag( civilian, "Panicking" ) ) throw new Exception( "Civilian saw hidden gun." );
	wall.Destroy(); Tick( 10 );
	if ( !Flag( civilian, "Panicking" ) ) throw new Exception( "Civilian ignored drawn gun." );
	inventory.Drop( gun ); gun.GameObject.Destroy(); civilian.GameObject.Destroy(); Tick( 1 );

	civilian = Spawn( "civilian", Vector3.Zero );
	player.WorldPosition = new Vector3( -60, 0, 0 );
	Tick( 1 );
	void Command() => civilian.GetType().GetMethod( "Command" ).Invoke( civilian, new object[] { player } );
	Command();
	if ( !Flag( civilian, "Surrendered" ) ) throw new Exception( "Test surrender failed." );
	var cop = Spawn( "cop", new Vector3( 250, 50, 0 ) );
	cop.WorldRotation = Rotation.FromYaw( 180 );
	var guard = Spawn( "security", new Vector3( 250, -50, 0 ) );
	guard.WorldRotation = Rotation.FromYaw( 180 );
	Tick( 10 );
	int rescuers = new[] { cop, guard }.Count( n => n.ActiveSchedule?.GetType().Name == "PaydayRescueCivilian" );
	if ( rescuers != 1 ) throw new Exception( $"Expected one reserved rescuer, got {rescuers}." );
	Tick( 130 );
	if ( Flag( civilian, "Surrendered" ) || !Flag( civilian, "Evacuating" ) )
		throw new Exception( "Officer failed to free civilian and start evacuation." );
	civilian.GameObject.Destroy(); cop.GameObject.Destroy(); guard.GameObject.Destroy(); Tick( 1 );

	civilian = Spawn( "civilian", Vector3.Zero );
	player.WorldPosition = new Vector3( -60, 0, 0 );
	Tick( 1 ); Command(); Command();
	cop = Spawn( "cop", new Vector3( 100, 0, 0 ) ); cop.WorldRotation = Rotation.FromYaw( 180 );
	Tick( 100 );
	if ( !Flag( civilian, "Surrendered" ) || cop.ActiveSchedule?.GetType().Name == "PaydayRescueCivilian" )
		throw new Exception( "Officer stole a civilian actively following the player." );
	// A fresh rescue must yield to a newly exposed armed hostile.
	civilian.GameObject.Destroy(); cop.GameObject.Destroy(); Tick( 1 );
	civilian = Spawn( "civilian", Vector3.Zero );
	player.WorldPosition = new Vector3( -60, 0, 0 );
	Tick( 1 ); Command();
	cop = Spawn( "cop", new Vector3( 200, 0, 0 ) ); cop.WorldRotation = Rotation.FromYaw( 180 );
	Tick( 10 );
	if ( cop.ActiveSchedule?.GetType().Name != "PaydayRescueCivilian" ) throw new Exception( "Interruption test did not start a rescue." );
	player.WorldPosition = new Vector3( -200, 300, 0 );
	gun = GameObject.GetPrefab( "mount://payday/entities/weapons/crosskill.prefab" ).Clone().GetComponent<BaseCombatWeapon>();
	inventory.Add( gun ); inventory.Switch( gun );
	Tick( 20 );
	if ( cop.GetDisposition( player ) != Sandbox.Npcs.Disposition.Hostile || cop.ActiveSchedule?.GetType().Name == "PaydayRescueCivilian" )
		throw new Exception( $"Rescue did not yield to armed threat: {cop.GetDisposition(player)}, {cop.ActiveSchedule}, cop={cop.WorldPosition}, player={player.WorldPosition}, forward={cop.WorldRotation.Forward}." );
	if ( !Flag( civilian, "Surrendered" ) ) throw new Exception( "Rescue completed during combat." );
	return "PASS: civilian gun visibility, exclusive rescue, release/evacuation, following protection, and combat interruption.";
}
finally { scene.Destroy(); }
