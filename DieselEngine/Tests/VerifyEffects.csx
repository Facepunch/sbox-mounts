var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
var resources = mount.GetAll( Sandbox.Mounting.ResourceType.GameResource ).Where( r => r.Path.EndsWith( ".pdeffect" ) ).ToArray();
if ( resources.Length != 209 ) throw new Exception( $"Expected 209 original effects, found {resources.Length}." );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var camera = new GameObject( true, "Camera" ).AddComponent<CameraComponent>();
	camera.WorldPosition = new Vector3( 180, -180, 100 );
	camera.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, 30 ) - camera.WorldPosition );
	camera.BackgroundColor = new Color( .07f, .09f, .13f );
	var results = new System.Collections.Generic.List<object>();
	foreach ( var path in new[] { "effects/particles/window/bank_window_vault", "effects/particles/dest/security_camera_dest" } )
	{
		var go = new GameObject( false, "Original particle effect" );
		var component = go.Components.Create( TypeLibrary.GetType( "Diesel.PaydayEffect" ) );
		component.GetType().GetProperty( "EffectPath" ).SetValue( component, path );
		component.GetType().GetProperty( "DestroyWhenFinished" ).SetValue( component, false );
		go.Enabled = true;
		int peak = 0;
		for ( int i = 0; i < 100; i++ )
		{
			scene.GameTick( .02 ); await Task.Delay( 10 );
			peak = Math.Max( peak, (int)component.GetType().GetProperty( "ParticleCount" ).GetValue( component ) );
			if ( i == 25 )
			{
				var sprites = scene.GetAllComponents<SpriteRenderer>().ToArray();
				if ( sprites.Length == 0 || sprites.Any( s => s.Sprite is null ) )
					throw new Exception( "Effect has missing sprite textures." );
				using var bitmap = new Bitmap( 900, 700 );
				camera.RenderToBitmap( bitmap );
				System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"payday-effect-{path.Split('/').Last()}.png" ), bitmap.ToPng() );
			}
		}
		if ( peak == 0 || peak > 512 ) throw new Exception( $"Invalid particle count for {path}: {peak}" );
		go.Enabled = false;
		if ( (int)component.GetType().GetProperty( "ParticleCount" ).GetValue( component ) != 0 )
			throw new Exception( "Disabled emitter leaked particles." );
		go.Destroy();
		results.Add( new { Path = path, Peak = peak } );
	}
	return new { Definitions = resources.Length, Results = results };
}
finally { scene.Destroy(); }
