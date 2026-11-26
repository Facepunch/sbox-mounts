await Sandbox.Mounting.Directory.Mount( "payday" );
foreach ( var id in new[] { "shield", "bulldozer" } )
	await ResourceLibrary.LoadAsync<PrefabFile>( $"mount://payday/entities/npcs/{id}.prefab" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var shield = GameObject.GetPrefab( "mount://payday/entities/npcs/shield.prefab" ).Clone( new Vector3( 0, -55, 0 ), Rotation.Identity );
	var tank = GameObject.GetPrefab( "mount://payday/entities/npcs/bulldozer.prefab" ).Clone( new Vector3( 0, 55, 0 ), Rotation.Identity );
	var light = new GameObject( true, "Key" ).AddComponent<DirectionalLight>();
	light.WorldRotation = Rotation.From( 45, 150, 0 ); light.LightColor = Color.White * 3;
	var fill = new GameObject( true, "Fill" ).AddComponent<PointLight>();
	fill.WorldPosition = new Vector3( 80, -100, 90 ); fill.Radius = 500; fill.LightColor = Color.White * 3;
	var camera = new GameObject( true, "Camera" ).AddComponent<CameraComponent>();
	camera.WorldPosition = new Vector3( 200, -90, 105 );
	camera.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, 40 ) - camera.WorldPosition );
	camera.BackgroundColor = new Color( .12f, .14f, .18f );
	for ( int i = 0; i < 10; i++ ) scene.GameTick( .05 );
	using var bitmap = new Bitmap( 1000, 700 );
	camera.RenderToBitmap( bitmap );
	var path = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", "payday-specials.png" );
	System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
	((Component.IDamageable)tank.GetComponent<Sandbox.Npcs.BaseNpc>()).OnDamage(
		new DamageInfo { Damage = 200, Tags = new TagSet( ["bullet", "payday_visor"] ) } );
	camera.RenderToBitmap( bitmap );
	var brokenPath = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", "payday-specials-broken.png" );
	System.IO.File.WriteAllBytes( brokenPath, bitmap.ToPng() );
	return new { Intact = path, Broken = brokenPath };
}
finally { scene.Destroy(); }
