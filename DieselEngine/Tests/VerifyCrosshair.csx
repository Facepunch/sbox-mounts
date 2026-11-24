// Render the installed HUD atlas crop at the original weapon-specific offsets.
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var camera = new GameObject( true, "HUD verification" ).AddComponent<CameraComponent>();
	camera.BackgroundColor = new Color( .1f, .15f, .2f );
	var atlas = await ResourceLibrary.LoadAsync<Texture>( "mount://payday/guis/textures/hud_icons.texture.vtex" );
	if ( atlas is null || atlas.Width < 504 || atlas.Height < 37 ) throw new Exception( "Original HUD atlas unavailable." );
	var results = new System.Collections.Generic.List<string>();
	foreach ( var id in new[] { "amcar4", "b9s", "reinbeck", "gl40" } )
	{
		await ResourceLibrary.LoadAsync<PrefabFile>( $"mount://payday/entities/weapons/{id}.prefab" );
		var gun = GameObject.GetPrefab( $"mount://payday/entities/weapons/{id}.prefab" ).Clone();
		var weapon = gun.GetComponent<BaseCombatWeapon>();
		gun.Enabled = false;
		scene.EditorTick( RealTime.Now, 1f / 60 );
		using var bitmap = new Bitmap( 960, 540 );
		using ( var painter = camera.BeginHud() ) weapon.DrawCrosshair( painter, new Vector2( 480, 270 ) );
		camera.RenderToBitmap( bitmap );
		var path = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"crosshair-{id}.png" );
		System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
		results.Add( path );
		gun.Destroy();
		scene.ProcessDeletes();
	}
	return results;
}
finally { scene.Destroy(); }
