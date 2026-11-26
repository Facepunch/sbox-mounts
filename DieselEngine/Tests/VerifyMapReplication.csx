// Exercises relay reconstruction in a disposable scene. This is not a two-process network test.
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
	var relay = new GameObject( true, "Replica relay" ).AddComponent<Diesel.PaydayMapState>();
	void Apply() => typeof( Diesel.PaydayMapState ).GetMethod( "ApplyReplicatedState", flags ).Invoke( relay, null );
	var root = new GameObject( true, "Replicated door" );
	var door = root.AddComponent<Diesel.PaydayDoor>();
	door.Leaf = new GameObject( true, "Leaf" ) { Parent = root };
	door.Leaf.AddComponent<ModelRenderer>().Model = Model.Cube;
	door.Leaf.AddComponent<ModelCollider>().Model = Model.Cube;
	door.Pivot = new Vector3( 0, -16, 0 );
	string DoorState( bool open, float amount ) => Json.Serialize( new
	{
		Open = open, Amount = amount, Blocked = false, Direction = -1f, Resting = false,
		OpenAngle = 96f, RootPose = new Transform( new Vector3( 100, 200, 0 ), Rotation.FromYaw( 37 ) )
	} );
	relay.DoorStates[root.Id] = DoorState( true, .6f );
	Apply();
	if ( !door.Open || MathF.Abs( door.Amount - .6f ) > .001f || root.LocalPosition.Distance( new Vector3( 100, 200, 0 ) ) > .01f )
		throw new Exception( "Door join state did not restore state/root pose." );
	var pose = door.Leaf.LocalTransform;
	typeof( Diesel.PaydayDoor ).GetMethod( "OnStart", flags ).Invoke( door, null );
	if ( door.Leaf.LocalTransform != pose || door.Amount != .6f ) throw new Exception( "Door startup overwrote restored state." );
	if ( (float)typeof( Diesel.PaydayDoor ).GetField( "previousAmount", flags ).GetValue( door ) != .6f )
		throw new Exception( "Join state would replay movement audio." );
	relay.DoorStates[root.Id] = DoorState( false, .2f );
	Apply();
	if ( door.Open || door.Amount != .2f || door.Leaf.LocalTransform == pose )
		throw new Exception( "Door movement update did not reach collision transform." );

	var alarm = new GameObject( true, "Phone" ).AddComponent<Diesel.PaydayAlarm>();
	relay.AlarmStates[alarm.GameObject.Id] = Json.Serialize( new { Activated = true, Disabled = true } );
	var camera = new GameObject( true, "Camera" ).AddComponent<Diesel.PaydaySecurityCamera>();
	relay.CameraStates[camera.GameObject.Id] = Json.Serialize( new { Health = 0f, Disabled = true, Activated = true, Detecting = false } );
	Apply();
	if ( !alarm.Disabled || !alarm.Activated || !camera.Disabled || camera.Health != 0 || !camera.Activated )
		throw new Exception( "Phone/camera state did not restore." );

	// A disabled-unit snapshot can arrive before its target object exists.
	var delayedId = Guid.NewGuid();
	relay.DisabledUnits.Add( delayedId );
	Apply();
	var delayed = new GameObject( true, "Delayed unit" );
	typeof( GameObject ).GetMethod( "SetDeterministicId", flags ).Invoke( delayed, new object[] { delayedId } );
	Apply();
	if ( delayed.Enabled ) throw new Exception( "Early disable state was lost before the object arrived." );

	var hostPhone = new GameObject( true, "Host phone" ).AddComponent<Diesel.PaydayAlarm>();
	var player = new GameObject( true, "Phone user" );
	var controller = player.AddComponent<PlayerController>();
	controller.UseInputControls = false;
	player.NetworkSpawn( true, Connection.Local );
	player.WorldPosition = new Vector3( -500, 0, 0 );
	hostPhone.DisableAlarm( player );
	if ( hostPhone.Disabled ) throw new Exception( "Relay accepted a distant phone press." );
	player.WorldPosition = new Vector3( -60, 0, 0 );
	typeof( PlayerController ).GetProperty( "EyeTransform" ).SetValue( controller, new Transform( new Vector3( -60, 0, 64 ), Rotation.Identity ) );
	var wall = new GameObject( true, "Use blocker" );
	wall.WorldPosition = new Vector3( -30, 0, 40 );
	wall.AddComponent<BoxCollider>().Scale = new Vector3( 10, 100, 100 );
	await Task.Yield();
	hostPhone.DisableAlarm( player );
	if ( hostPhone.Disabled ) throw new Exception( "Relay accepted a phone press through a wall." );
	wall.Destroy();
	scene.GameTick( .05 );
	await Task.Yield();
	hostPhone.DisableAlarm( player );
	if ( !hostPhone.Disabled || !relay.AlarmStates.ContainsKey( hostPhone.GameObject.Id ) )
	{
		var trace = scene.Trace.Ray( controller.EyePosition, hostPhone.WorldPosition + Vector3.Up * 24 ).IgnoreGameObjectHierarchy( player ).WithoutTags( "trigger" ).Run();
		throw new Exception( $"Validated phone press failed: disabled={hostPhone.Disabled}, host={Networking.IsHost}, eye={controller.EyePosition}, player={player.WorldPosition}, hit={trace.GameObject?.Name}." );
	}
	return "PASS: door state/pose and movement, silent join baseline, startup ordering, phone/camera restoration, delayed disable target, host-validated phone relay.";
}
finally { scene.Destroy(); }
