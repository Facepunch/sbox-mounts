var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
await mount.RefreshInternal();
await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/npcs/civilian.prefab" );
await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/interactive/alarm_phone.prefab" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var map = new GameObject( true, "Shared imported map root" );
	var floor = new GameObject( true, "Floor" ) { Parent = map };
	var collider = floor.AddComponent<BoxCollider>();
	collider.Center = new Vector3( 0, 0, -10 ); collider.Scale = new Vector3( 2000, 2000, 20 );
	if ( !await scene.NavMesh.Generate( scene.PhysicsWorld ) ) throw new Exception( "Navmesh failed." );
	void Tick( int n ) { for ( int i = 0; i < n; i++ ) scene.GameTick( .05 ); }
	IDisposable Clock() => Time.Scope( (double)typeof(Scene).GetProperty( "TimeNow", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance ).GetValue( scene ), .05 );
	Sandbox.Npcs.BaseNpc Spawn( Vector3 position )
	{
		var go = GameObject.GetPrefab( "mount://payday/entities/npcs/civilian.prefab" ).Clone();
		go.WorldPosition = position;
		var npc = go.GetComponent<Sandbox.Npcs.BaseNpc>();
		npc.GetType().GetProperty( "Wander" ).SetValue( npc, false );
		return npc;
	}
	var threat = new GameObject( true, "Threat" );
	threat.WorldPosition = new Vector3( -400, 0, 0 );
	var first = Spawn( Vector3.Zero );
	var neighbor = Spawn( new Vector3( 0, 150, 0 ) );
	var hidden = Spawn( new Vector3( 0, -150, 0 ) );
	var wall = new GameObject( true, "Wall" ) { Parent = map };
	wall.WorldPosition = new Vector3( 0, -75, 80 );
	wall.AddComponent<BoxCollider>().Scale = new Vector3( 500, 20, 160 );
	Tick( 2 );
	using ( Clock() ) first.GetType().GetMethod( "Scare" ).Invoke( first, new object[] { threat } );
	bool Panic( Component c ) => (bool)c.GetType().GetProperty( "Panicking" ).GetValue( c );
	if ( !Panic( neighbor ) || Panic( hidden ) ) throw new Exception( "Visible-neighbor panic propagation failed." );
	first.GameObject.Destroy(); neighbor.GameObject.Destroy(); hidden.GameObject.Destroy(); wall.Destroy(); Tick( 1 );
	var reporter = Spawn( Vector3.Zero );
	var phone = GameObject.GetPrefab( "mount://payday/entities/interactive/alarm_phone.prefab" ).Clone();
	phone.Parent = map; phone.WorldPosition = new Vector3( 65, 0, 25 );
	var alarm = phone.Components.GetAll().First( c => c.GetType().Name == "PaydayAlarm" );
	var barrier = new GameObject( true, "Same-root obstruction" ) { Parent = map };
	barrier.WorldPosition = new Vector3( 32, 0, 50 );
	barrier.AddComponent<BoxCollider>().Scale = new Vector3( 10, 200, 100 );
	Tick( 1 );
	var report = alarm.GetType().GetMethod( "Report" );
	using ( Clock() )
		if ( (bool)report.Invoke( alarm, new object[] { reporter.GameObject, threat } ) ) throw new Exception( "Alarm accepted report through same-root wall." );
	var player = new GameObject( true, "Phone interaction player" );
	player.WorldPosition = new Vector3( -25, 0, 0 );
	player.AddComponent<PlayerController>().UseInputControls = false;
	player.NetworkSpawn( true, Connection.Local );
	Tick( 1 );
	var disable = alarm.GetType().GetMethod( "DisableAlarm" );
	disable.Invoke( alarm, new object[] { player } );
	if ( (bool)alarm.GetType().GetProperty( "Disabled" ).GetValue( alarm ) ) throw new Exception( "Player disabled phone through map wall." );
	barrier.Destroy(); Tick( 1 );
	var reserve = alarm.GetType().GetMethod( "Reserve", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic );
	using ( Clock() )
	{
		if ( !(bool)reserve.Invoke( alarm, new object[] { reporter.GameObject } ) ) throw new Exception( "Reservation failed." );
		if ( (bool)reserve.Invoke( alarm, new object[] { threat } ) ) throw new Exception( "Concurrent reservation accepted." );
		if ( !(bool)report.Invoke( alarm, new object[] { reporter.GameObject, threat } ) ) throw new Exception( "Unobstructed report failed." );
	}
	// Disable a fresh phone, then make sure the civilian chooses the escape point instead.
	phone.Destroy(); Tick( 1 );
	phone = GameObject.GetPrefab( "mount://payday/entities/interactive/alarm_phone.prefab" ).Clone();
	phone.Parent = map; phone.WorldPosition = new Vector3( 65, 0, 25 );
	alarm = phone.Components.GetAll().First( c => c.GetType().Name == "PaydayAlarm" );
	player.WorldPosition = new Vector3( 65, -60, 0 );
	Tick( 1 );
	disable.Invoke( alarm, new object[] { player } );
	if ( !(bool)alarm.GetType().GetProperty( "Disabled" ).GetValue( alarm ) ) throw new Exception( "Nearby player could not disable phone." );
	var exit = new GameObject( true, "Authored flee destination" ) { Parent = map };
	exit.WorldPosition = new Vector3( 300, 0, 0 );
	exit.Components.Create( TypeLibrary.GetType( "Diesel.PaydayEscapePoint" ) );
	reporter.GameObject.Destroy(); Tick( 1 );
	reporter = Spawn( Vector3.Zero );
	using ( Clock() ) reporter.GetType().GetMethod( "Scare" ).Invoke( reporter, new object[] { threat } );
	var escaping = reporter.GameObject;
	Tick( 150 );
	if ( !escaping.IsDestroyed ) throw new Exception( $"Civilian failed to escape: {reporter.WorldPosition}, {reporter.ActiveSchedule}" );
	var trappedPhone = GameObject.GetPrefab( "mount://payday/entities/interactive/alarm_phone.prefab" ).Clone();
	trappedPhone.WorldPosition = new Vector3( 400, 400, 25 );
	foreach ( var offset in new[] { new Vector3( 100, 0, 0 ), new Vector3( -100, 0, 0 ), new Vector3( 0, 100, 0 ), new Vector3( 0, -100, 0 ) } )
	{
		var enclosure = new GameObject( true, "Unreachable phone enclosure" );
		enclosure.WorldPosition = new Vector3( 400, 400, 80 ) + offset;
		enclosure.AddComponent<BoxCollider>().Scale = offset.x == 0 ? new Vector3( 220, 20, 160 ) : new Vector3( 20, 220, 160 );
	}
	if ( !await scene.NavMesh.Generate( scene.PhysicsWorld ) ) throw new Exception( "Enclosure navmesh failed." );
	var blocked = Spawn( Vector3.Zero );
	var blockedObject = blocked.GameObject;
	using ( Clock() ) blocked.GetType().GetMethod( "Scare" ).Invoke( blocked, new object[] { threat } );
	Tick( 350 );
	if ( !blockedObject.IsDestroyed ) throw new Exception( $"Unreachable phone did not fall back to escape: {blocked.ActiveSchedule}, {blocked.WorldPosition}" );
	return "PASS: visible civilian warnings, wall occlusion under shared map roots, exclusive phone reservation, reporting, player disabling and escape fallback for disabled/unreachable phones.";
}
finally { scene.Destroy(); }
