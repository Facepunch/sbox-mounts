await Sandbox.Mounting.Directory.Mount( "payday" );
var model = await ResourceLibrary.LoadAsync<Model>( "mount://payday/units/world/architecture/hospital/hospital_door_1.unit.vmdl" );
await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/npcs/civilian.prefab" );
if ( model is null || model.IsError ) throw new Exception( "Door model missing." );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var floor = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
	floor.Scale = new Vector3( 2000, 2000, 20 ); floor.Center = new Vector3( 0, 0, -10 );
	var root = new GameObject( true, "Authored hinge" );
	root.WorldPosition = new Vector3( 100, 150, 0 ); root.WorldRotation = Rotation.FromYaw( 37 );
	Vector3 Point( Vector3 p ) => root.WorldTransform.PointToWorld( p );
	var door = root.Components.Create( TypeLibrary.GetType( "Diesel.PaydayDoor" ) );
	var leaf = new GameObject( true, "Door leaf" ) { Parent = root };
	leaf.Tags.Add( "payday_door" );
	leaf.AddComponent<ModelRenderer>().Model = model;
	leaf.AddComponent<ModelCollider>().Model = model;
	door.GetType().GetProperty( "Leaf" ).SetValue( door, leaf );
	scene.NavMesh.ExcludedBodies.Add( "payday_door" );
	await scene.NavMesh.Generate( scene.PhysicsWorld );
	void Tick( int n ) { for ( int i = 0; i < n; i++ ) scene.GameTick( .05 ); }
	float Amount() => (float)door.GetType().GetProperty( "Amount" ).GetValue( door );
	var center = model.Bounds.Center;
	var player = new GameObject( true, "Player" );
	player.WorldPosition = Point( center.WithZ( 0 ) + Vector3.Left * 80 );
	player.AddComponent<PlayerController>().UseInputControls = false;
	player.NetworkSpawn( true, Connection.Local );
	void Toggle() => door.GetType().GetMethod( "Toggle" ).Invoke( door, new object[] { player } );
	Tick( 2 );
	var start = Point( center + Vector3.Left * 100 );
	var end = Point( center - Vector3.Left * 100 );
	if ( scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( player ).Run().GameObject != leaf )
		throw new Exception( "Closed door lacks solid leaf collision." );
	player.WorldPosition = Point( center.WithZ( 0 ) + Vector3.Left * 500 );
	Tick( 1 );
	Toggle(); Tick( 15 );
	if ( Amount() != 0 ) throw new Exception( "Distant player opened door." );
	player.WorldPosition = Point( center.WithZ( 0 ) + Vector3.Left * 80 );
	Tick( 1 );
	var wall = new GameObject( true, "Use obstruction" );
	wall.WorldPosition = Point( center.WithZ( 40 ) + Vector3.Left * 40 ); wall.WorldRotation = root.WorldRotation;
	wall.AddComponent<BoxCollider>().Scale = new Vector3( 150, 10, 100 );
	Toggle(); Tick( 15 );
	if ( Amount() != 0 ) throw new Exception( "Player used door through obstruction." );
	wall.Destroy(); Tick( 1 );
	var view = new GameObject( true, "Door preview" ).AddComponent<CameraComponent>();
	view.WorldPosition = Point( center + new Vector3( 120, 160, 30 ) );
	view.WorldRotation = Rotation.LookAt( Point( center ) - view.WorldPosition );
	view.BackgroundColor = new Color( .12f, .15f, .2f );
	var light = new GameObject( true, "Door light" ).AddComponent<PointLight>();
	light.WorldPosition = view.WorldPosition; light.Radius = 600; light.LightColor = Color.White * 4;
	void Screenshot( string state )
	{
		using var bitmap = new Bitmap( 512, 512 );
		view.RenderToBitmap( bitmap );
		System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"door-{state}.png" ), bitmap.ToPng() );
	}
	Tick( 1 ); Screenshot( "closed" );
	Toggle(); Tick( 15 );
	Screenshot( "open" );
	if ( Amount() < .99f ) throw new Exception( $"Player door opening failed: {Amount()}, bounds={model.Bounds}." );
	if ( scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( player ).Run().GameObject == leaf )
		throw new Exception( "Open door retained closed collision." );
	Toggle(); Tick( 15 );
	if ( Amount() > .01f ) throw new Exception( "Door did not close." );
	Toggle(); Tick( 15 );
	player.WorldPosition = Point( center.WithZ( 0 ) );
	Tick( 1 );
	Toggle(); Tick( 25 );
	if ( Amount() < .9f ) throw new Exception( "Closing door did not reopen for occupant." );
	player.WorldPosition = Point( center.WithZ( 0 ) + Vector3.Left * 80 );
	Tick( 1 );
	Toggle(); Tick( 15 );
	if ( Amount() > .01f ) throw new Exception( "Cleared doorway could not close." );

	var npcObject = GameObject.GetPrefab( "mount://payday/entities/npcs/civilian.prefab" )
		.Clone( Point( center.WithZ( 0 ) - Vector3.Left * 250 ) );
	var npc = npcObject.GetComponent<Sandbox.Npcs.BaseNpc>();
	npc.GetType().GetProperty( "Wander" ).SetValue( npc, false );
	player.WorldPosition = npcObject.WorldPosition + root.WorldRotation * Vector3.Left * 60;
	Tick( 2 );
	var command = npc.GetType().GetMethod( "Command" );
	command.Invoke( npc, new object[] { player } ); command.Invoke( npc, new object[] { player } );
	player.WorldPosition = Point( center.WithZ( 0 ) + Vector3.Left * 500 );
	Tick( 150 );
	if ( Amount() < .99f || root.WorldTransform.PointToLocal( npcObject.WorldPosition ).y < center.y + 70 )
		throw new Exception( $"Following NPC failed to open/pass door: amount={Amount()}, npc={npcObject.WorldPosition}, schedule={npc.ActiveSchedule}." );
	return "PASS: original hospital leaf collision, range-checked use, open/close, occupant reopening and NPC navmesh passage.";
}
finally { scene.Destroy(); }
