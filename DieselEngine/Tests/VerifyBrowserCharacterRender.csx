await Sandbox.Mounting.Directory.Mount( "payday" );
var model = await ResourceLibrary.LoadAsync<Model>( "mount://payday/units/characters/civilians/builder_1/builder_1.unit.vmdl" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var renderer = new GameObject( true, "Browser character" ).AddComponent<SkinnedModelRenderer>();
	renderer.Model = model;
	var camera = new GameObject( true, "Camera" ).AddComponent<CameraComponent>();
	camera.WorldPosition = new Vector3( 110, -145, 65 );
	camera.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, 38 ) - camera.WorldPosition );
	camera.BackgroundColor = new Color( .12f, .15f, .2f );
	var light = new GameObject( true, "Light" ).AddComponent<DirectionalLight>();
	light.WorldRotation = Rotation.From( 35, 45, 0 );
	light.LightColor = Color.White * 2;
	var fill = new GameObject( true, "Fill" ).AddComponent<PointLight>();
	fill.WorldPosition = new Vector3( 80, -80, 90 ); fill.Radius = 500; fill.LightColor = Color.White * 2;
	scene.GameTick( .02 );
	using var bitmap = new Bitmap( 512, 512 );
	camera.RenderToBitmap( bitmap );
	var path = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", "browser-builder.png" );
	System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
	return path;
}
finally { scene.Destroy(); }
