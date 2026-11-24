// Rebuild and render isolated map scenes. The user's active scene is not replaced.
await Sandbox.Mounting.Directory.Mount( "payday" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var output = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode" );
System.IO.Directory.CreateDirectory( output );
var results = new System.Collections.Generic.List<object>();
foreach ( var level in new[] { "bank", "diamondheist" } )
{
	var loader = Sandbox.Mounting.MountUtility.FindLoader( $"mount://payday/levels/{level}/world.world.scene" );
	loader.GetType().GetMethod( "OnSceneUnloaded", flags ).Invoke( loader, null );
	var file = loader.GetType().GetMethod( "Load", flags ).Invoke( loader, null ) as SceneFile;
	if ( file is null ) throw new Exception( "Map build failed: " + level );
	var scene = new Scene();
	try
	{
		using var scope = scene.Push();
		if ( !scene.Load( file ) ) throw new Exception( "Map deserialization failed." );
		var sun = scene.GetAllComponents<DirectionalLight>().Single();
		if ( scene.GetAllComponents<AmbientLight>().Any() ) throw new Exception( "Preview ambient double-lights the authored sky." );
		float expectedRed = level == "bank" ? 1.5f : 0.59694308f * 0.538f;
		if ( MathF.Abs( sun.LightColor.r - expectedRed ) > 0.001f ) throw new Exception( "Authored sunlight was lost during scene serialization." );
		var materials = loader.GetType().GetField( "materials", flags ).GetValue( loader );
		var cache = (System.Collections.IDictionary)materials.GetType().GetField( "materials", flags ).GetValue( materials );
		var sky = cache.Values.Cast<Material>().First( m => m is not null ).GetVector4( "g_vEnvironmentSkyTop" );
		if ( sky.x <= 0 || sky.y <= 0 || sky.z <= 0 ) throw new Exception( "Sky colours were not bound." );
		var camera = new GameObject( true, "Environment verification" ).AddComponent<CameraComponent>();
		camera.WorldTransform = scene.GetAllComponents<SpawnPoint>().First().WorldTransform;
		camera.WorldPosition += Vector3.Up * 64;
		camera.FieldOfView = 90;
		camera.ZFar = 100000;
		scene.Enabled = true;
		using var bitmap = new Bitmap( 1280, 720 );
		for ( int i = 0; i < 4; i++ ) { await Task.Yield(); camera.RenderToBitmap( bitmap ); }
		string screenshot = System.IO.Path.Combine( output, level + "-environment.png" );
		System.IO.File.WriteAllBytes( screenshot, bitmap.ToPng() );
		results.Add( new { Level = level, Sun = sun.LightColor.ToString(), SkyTop = sky.ToString(), Screenshot = screenshot } );
	}
	finally { scene.Destroy(); }
}
return results;
