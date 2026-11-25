// Exercise every placed-open apartment leaf, not just one representative per shared model.
await Sandbox.Mounting.Directory.Mount( "payday" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var loader = Sandbox.Mounting.MountUtility.FindLoader( "mount://payday/levels/apartment/world.world.scene" );
loader.GetType().GetMethod( "OnSceneUnloaded", flags ).Invoke( loader, null );
SceneFile file;
try { file = (SceneFile)loader.GetType().GetMethod( "Load", flags ).Invoke( loader, null ); }
catch ( System.Reflection.TargetInvocationException e ) { throw new Exception( e.InnerException?.ToString(), e ); }
var imported = new Scene();
var results = new System.Collections.Generic.List<string>();
try
{
	using var importScope = imported.Push();
	imported.Load( file );
	var crookedNames = new[] { "apartment_door_01_010", "apartment_door_02_014", "apartment_door_02_010", "apartment_door_02_015" };
	foreach ( string name in crookedNames )
	{
		var g = imported.GetAllObjects( true ).Single( g => g.Name == name );
		if ( !g.Components.GetAll().Any( c => c.GetType().Name == "PaydayDoor" ) )
			throw new Exception( $"{name}: mildly tilted attached door was excluded." );
	}
	// Resolve against the actual loaded architecture before copying doors to isolated test scenes.
	foreach ( var door in imported.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() )
		.Where( c => c.GetType().Name == "PaydayDoor" && (bool)c.GetType().GetProperty( "ResolveDoorway" ).GetValue( c ) ) )
	{
		var leaf = (GameObject)door.GetType().GetProperty( "Leaf" ).GetValue( door );
		var placed = leaf.WorldTransform;
		door.GetType().GetMethod( "OnStart", flags ).Invoke( door, null );
		if ( leaf.WorldPosition.Distance( placed.Position ) > .01f
			|| Vector3.Dot( leaf.WorldRotation.Forward, placed.Rotation.Forward ) < .9999f
			|| Vector3.Dot( leaf.WorldRotation.Up, placed.Rotation.Up ) < .9999f )
			throw new Exception( $"{door.GameObject.Name}: resolving the doorway changed the placed pose/tilt." );
	}
	var sources = imported.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() )
		.Where( c => c.GetType().Name == "PaydayDoor"
			&& ((float)c.GetType().GetProperty( "InitialOpenAngle" ).GetValue( c ) != 0 || crookedNames.Contains( c.GameObject.Name )) ).ToArray();
	var reported = sources.Single( c => c.GameObject.Name == "apartment_door_01_005" );
	if ( Vector3.Dot( reported.GameObject.WorldRotation.Forward, Rotation.FromYaw( -90 ).Forward ) < .999f
		|| MathF.Abs( (float)reported.GetType().GetProperty( "InitialOpenAngle" ).GetValue( reported ) - 101 ) > .1f )
		throw new Exception( "Reported kitchen door must close at -90 degrees and start 101 degrees open, rather than 11 degrees from the wall." );
	foreach ( var source in sources )
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push(); scene.Enabled = true;
			var root = source.GameObject.Clone( new Vector3( 100, 100, 0 ), Rotation.FromYaw( 37 ) );
			var door = root.Components.GetAll().Single( c => c.GetType().Name == "PaydayDoor" );
			var type = door.GetType();
			var leaf = (GameObject)type.GetProperty( "Leaf" ).GetValue( door );
			var model = leaf.GetComponent<ModelRenderer>().Model;
			var initial = leaf.LocalTransform;
			void Tick() { for ( int i = 0; i < 20; i++ ) scene.GameTick( .05 ); }
			float Amount() => (float)type.GetProperty( "Amount" ).GetValue( door );
			Tick();
			if ( leaf.LocalPosition.Distance( initial.Position ) > .01f
				|| Vector3.Dot( leaf.LocalRotation.Forward, initial.Rotation.Forward ) < .9999f )
				throw new Exception( $"{root.Name}: startup moved the placed leaf." );
			var player = new GameObject( true, "Player" );
			player.AddComponent<PlayerController>().UseInputControls = false;
			player.NetworkSpawn( true, Connection.Local );
			void Use()
			{
				var normal = model.Bounds.Size.x < model.Bounds.Size.y ? Vector3.Forward : Vector3.Left;
				player.WorldPosition = leaf.WorldTransform.PointToWorld( model.Bounds.Center ) + leaf.WorldRotation * normal * 70 - Vector3.Up * 64;
				scene.GameTick( .05 );
				type.GetMethod( "Toggle" ).Invoke( door, new object[] { player } );
				player.WorldPosition = new Vector3( 800, 800, 0 ); Tick();
			}
			if ( (float)type.GetProperty( "InitialOpenAngle" ).GetValue( door ) != 0 ) Use();
			if ( Amount() > .001f ) throw new Exception( $"{root.Name}: first use opened instead of closing; initial={type.GetProperty("InitialOpenAngle").GetValue(door)}, amount={Amount()}." );
			Use();
			if ( Amount() < .999f ) throw new Exception( $"{root.Name}: could not reopen." );
			Use();
			if ( Amount() > .001f ) throw new Exception( $"{root.Name}: could not close again." );
			results.Add( $"{root.Name}: startup, close-first, reopen and close PASS" );
		}
		finally { scene.Destroy(); }
	}
	if ( sources.Length == 0 ) throw new Exception( "No placed-open doors tested." );
}
finally { imported.Destroy(); }
return results;
