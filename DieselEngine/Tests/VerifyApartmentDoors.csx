await Sandbox.Mounting.Directory.Mount( "payday" );
await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/npcs/civilian.prefab" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var results = new System.Collections.Generic.List<string>();
foreach ( var (level, count) in new[] { ("bank", 12), ("diamondheist", 10), ("street", 5), ("bridge", 1), ("slaughterhouse", 24), ("apartment", 57) } )
{
	var loader = Sandbox.Mounting.MountUtility.FindLoader( $"mount://payday/levels/{level}/world.world.scene" );
	loader.GetType().GetMethod( "OnSceneUnloaded", flags ).Invoke( loader, null );
	var file = (SceneFile)loader.GetType().GetMethod( "Load", flags ).Invoke( loader, null );
	var imported = new Scene();
	try
	{
		using var importScope = imported.Push();
		if ( !imported.Load( file ) ) throw new Exception( $"{level} load failed." );
		foreach ( var door in imported.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() )
			.Where( c => c.GetType().Name == "PaydayDoor" && (bool)c.GetType().GetProperty( "ResolveDoorway" ).GetValue( c ) ) )
			door.GetType().GetMethod( "OnStart", flags ).Invoke( door, null );
		var doors = imported.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() )
			.Where( c => c.GetType().Name == "PaydayDoor" && (float)c.GetType().GetProperty( "FixedOpenAngle" ).GetValue( c ) != 0
				&& !c.GameObject.Children.Any( g => g.Name == "Door frame" ) ).ToArray();
		if ( doors.Length != count ) throw new Exception( $"{level}: expected {count}, found {doors.Length} apartment doors." );
		if ( level is "slaughterhouse" or "apartment" )
		foreach ( var source in doors.GroupBy( d => ((GameObject)d.GetType().GetProperty( "Leaf" ).GetValue( d )).GetComponent<ModelRenderer>().Model ).Select( g => g.First() ) )
		{
			var scene = new Scene();
			try
			{
				using var scope = scene.Push(); scene.Enabled = true;
				var floor = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
				floor.Scale = new Vector3( 2000, 2000, 20 ); floor.Center = new Vector3( 0, 0, -10 );
				var root = source.GameObject.Clone( new Vector3( 100, 100, 0 ), Rotation.FromYaw( 37 ) );
				var door = root.Components.GetAll().Single( c => c.GetType().Name == "PaydayDoor" );
				var type = door.GetType();
				var leaf = (GameObject)type.GetProperty( "Leaf" ).GetValue( door );
				var model = leaf.GetComponent<ModelRenderer>().Model;
				float angle = (float)type.GetProperty( "FixedOpenAngle" ).GetValue( door );
				float initialAngle = (float)type.GetProperty( "InitialOpenAngle" ).GetValue( door );
				bool startsOpen = initialAngle != 0;
				if ( model.IsError || model.MeshCount == 0 || MathF.Abs( angle ) < 90 || (bool)type.GetProperty( "OpenBothWays" ).GetValue( door ) )
					throw new Exception( "Invalid apartment door geometry/swing." );
				if ( source.GameObject.Name.Contains( "apartment_door_04" ) && model.MeshCount < 2 )
					throw new Exception( "Glazed door lost its separate glass material batch." );
				var across = model.Bounds.Size.x < model.Bounds.Size.y ? Vector3.Forward : Vector3.Left;
				var normal = root.WorldRotation * across;
				var center = root.WorldTransform.PointToWorld( model.Bounds.Center );
				void Tick( int n ) { for ( int i = 0; i < n; i++ ) scene.GameTick( .05 ); }
				float Amount() => (float)type.GetProperty( "Amount" ).GetValue( door );
				var initialForward = Rotation.FromYaw( initialAngle ).Forward;
				if ( Vector3.Dot( leaf.LocalRotation.Forward, initialForward ) < .999f )
					throw new Exception( "Serialized starting pose was lost." );
				Tick( 2 );
				angle = (float)type.GetProperty( "OpenAngle" ).GetValue( door ) * MathF.Sign( angle );
				float initialAmount = startsOpen ? MathF.Abs( initialAngle / angle ) : 0;
				if ( MathF.Abs( Amount() - initialAmount ) > .001f || Vector3.Dot( leaf.LocalRotation.Forward, initialForward ) < .999f )
					throw new Exception( "Runtime starting pose was lost." );
				var player = new GameObject( true, "Player" );
				player.AddComponent<PlayerController>().UseInputControls = false;
				player.NetworkSpawn( true, Connection.Local );
				void Use( float side )
				{
					player.WorldPosition = leaf.WorldTransform.PointToWorld( model.Bounds.Center ).WithZ( 0 ) + leaf.WorldRotation * across * (side * 70);
					Tick( 1 );
					type.GetMethod( "Toggle" ).Invoke( door, new object[] { player } );
					player.WorldPosition = new Vector3( 800, 800, 0 ); Tick( 20 );
				}
				if ( startsOpen ) Use( 1 );
				if ( Amount() != 0 || scene.Trace.Ray( center + normal * 80, center - normal * 80 ).Run().GameObject != leaf )
					throw new Exception( "Closed door collision missing." );
				var view = new GameObject( true, "Door preview" ).AddComponent<CameraComponent>();
				view.WorldPosition = center + normal * 180 + Vector3.Up * 20;
				view.WorldRotation = Rotation.LookAt( center - view.WorldPosition );
				var light = new GameObject( true, "Light" ).AddComponent<PointLight>();
				light.WorldPosition = view.WorldPosition; light.Radius = 600; light.LightColor = Color.White * 2;
				void Screenshot( string state )
				{
					var target = leaf.GetComponent<ModelRenderer>().Bounds.Center;
					view.WorldPosition = target + leaf.WorldRotation * across * 180 + Vector3.Up * 20;
					view.WorldRotation = Rotation.LookAt( target - view.WorldPosition );
					light.WorldPosition = view.WorldPosition;
					using var bitmap = new Bitmap( 600, 600 ); view.RenderToBitmap( bitmap );
					System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"apartment-door-{source.GameObject.Name}-{state}.png" ), bitmap.ToPng() );
				}
				Screenshot( "closed" );
				foreach ( float side in new[] { 1f, -1f } )
				{
					Use( side );
					if ( Amount() < .99f || Vector3.Dot( leaf.LocalRotation.Forward, Rotation.FromYaw( angle ).Forward ) < .999f )
						throw new Exception( "Wrong swing direction from one side." );
					if ( scene.Trace.Ray( center + normal * 80, center - normal * 80 ).Run().GameObject == leaf )
						throw new Exception( "Opening retained closed collision." );
					Screenshot( "open" );
					Use( side );
					if ( Amount() > .01f ) throw new Exception( "Door failed to close." );
				}
				await scene.NavMesh.Generate( scene.PhysicsWorld );
				var npcObject = GameObject.GetPrefab( "mount://payday/entities/npcs/civilian.prefab" ).Clone( center.WithZ( 0 ) - normal * 250 );
				var npc = npcObject.GetComponent<Sandbox.Npcs.BaseNpc>();
				npc.GetType().GetProperty( "Wander" ).SetValue( npc, false );
				player.WorldPosition = npcObject.WorldPosition + normal * 60; Tick( 2 );
				var command = npc.GetType().GetMethod( "Command" );
				command.Invoke( npc, new object[] { player } ); command.Invoke( npc, new object[] { player } );
				player.WorldPosition = center.WithZ( 0 ) + normal * 500; Tick( 150 );
				if ( Amount() < .99f || Vector3.Dot( npcObject.WorldPosition - center, normal ) < 70 )
					throw new Exception( "NPC could not open/pass apartment door." );
				results.Add( $"{source.GameObject.Name}: swing {angle}, startsOpen={startsOpen}, both-side use, collision and NPC passage PASS" );
			}
			finally { scene.Destroy(); }
		}
		results.Add( $"{level}: {doors.Length} authored apartment doors PASS" );
	}
	finally { imported.Destroy(); }
}
return results;
