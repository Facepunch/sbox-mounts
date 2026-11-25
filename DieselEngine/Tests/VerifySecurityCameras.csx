var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
await mount.RefreshInternal();
await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/npcs/security.prefab" );
await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/weapons/crosskill.prefab" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var map = new GameObject( true, "Map root" );
	var floor = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
	floor.Scale = new Vector3( 3000, 3000, 20 ); floor.Center = new Vector3( 0, 0, -10 );
	await scene.NavMesh.Generate( scene.PhysicsWorld );
	void Tick( int n ) { for ( int i = 0; i < n; i++ ) scene.GameTick( .05 ); }
	bool Flag( Component c, string name ) => (bool)c.GetType().GetProperty( name ).GetValue( c );
	Component Camera()
	{
		var go = new GameObject( true, "Test security camera" ) { Parent = map };
		go.WorldPosition = new Vector3( 0, 0, 64 );
		go.AddComponent<BoxCollider>().Scale = new Vector3( 10, 10, 10 );
		return go.Components.Create( TypeLibrary.GetType( "Diesel.PaydaySecurityCamera" ) );
	}
	var camera = Camera();
	var player = new GameObject( true, "Player" );
	player.WorldPosition = new Vector3( 300, 0, 0 );
	player.AddComponent<PlayerController>().UseInputControls = false;
	player.NetworkSpawn( true, Connection.Local );
	var inventory = player.AddComponent<BaseInventoryComponent>(); inventory.GiveOnStart = false;
	Tick( 40 );
	if ( Flag( camera, "Activated" ) || Flag( camera, "Detecting" ) ) throw new Exception( "Camera reported unarmed player." );
	var gun = GameObject.GetPrefab( "mount://payday/entities/weapons/crosskill.prefab" ).Clone().GetComponent<BaseCombatWeapon>();
	inventory.Add( gun ); inventory.Switch( gun );
	player.WorldPosition = new Vector3( -300, 0, 0 );
	Tick( 40 );
	if ( Flag( camera, "Activated" ) ) throw new Exception( "Camera saw behind itself." );
	player.WorldPosition = new Vector3( 300, 0, 0 );
	var wall = new GameObject( true, "Same-root obstruction" ) { Parent = map };
	wall.WorldPosition = new Vector3( 150, 0, 80 );
	wall.AddComponent<BoxCollider>().Scale = new Vector3( 20, 500, 160 );
	Tick( 40 );
	if ( Flag( camera, "Activated" ) || Flag( camera, "Detecting" ) ) throw new Exception( "Camera saw through wall." );
	wall.Enabled = false;
	Tick( 15 );
	if ( Flag( camera, "Activated" ) || !Flag( camera, "Detecting" ) ) throw new Exception( "Detection delay missing." );
	wall.Enabled = true; Tick( 10 ); wall.Enabled = false;
	Tick( 15 );
	if ( Flag( camera, "Activated" ) ) throw new Exception( "Obstruction did not reset detection timer." );
	var guard = GameObject.GetPrefab( "mount://payday/entities/npcs/security.prefab" ).Clone().GetComponent<Sandbox.Npcs.BaseCombatNpc>();
	guard.WorldPosition = new Vector3( -400, 0, 0 ); guard.WorldRotation = Rotation.FromYaw( 180 );
	Tick( 60 );
	var seen = camera.GetType().GetField( "visibleFor", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic ).GetValue( camera );
	if ( !Flag( camera, "Activated" ) || guard.GetDisposition( player ) != Sandbox.Npcs.Disposition.Hostile )
		throw new Exception( $"Camera failed to report offender to nearby guard: activated={Flag(camera,"Activated")}, detecting={Flag(camera,"Detecting")}, guard={guard.GetDisposition(player)}, sound={guard.Senses?.Disturbance}, seen={seen}, delay={camera.GetType().GetProperty("DetectionDelay").GetValue(camera)}." );
	((Component.IDamageable)camera).OnDamage( new DamageInfo { Damage = 100, Attacker = player } );
	Tick( 1 );
	if ( !Flag( camera, "Disabled" ) || Flag( camera, "Detecting" ) ) throw new Exception( "Camera survived destruction." );
	camera.GameObject.Destroy(); guard.GameObject.Destroy(); Tick( 1 );
	camera = Camera();
	Tick( 1 );
	for ( int i = 0; i < 2; i++ )
	{
		var hit = BaseCombatWeapon.ShootBullet( scene, new Ray( player.WorldPosition + Vector3.Up * 64, Vector3.Backward ), 1000, 0, 20, 0, player, gun.GameObject );
		if ( hit.GameObject != camera.GameObject ) throw new Exception( $"Bullet hit {hit.GameObject} instead of camera." );
	}
	if ( !Flag( camera, "Disabled" ) ) throw new Exception( "Bullet damage did not disable camera." );
	Tick( 50 );
	if ( Flag( camera, "Activated" ) ) throw new Exception( "Destroyed camera continued detecting." );
	return "PASS: unarmed neutrality, view cone, wall occlusion, continuous detection delay, guard alert and damage disabling.";
}
finally { scene.Destroy(); }
