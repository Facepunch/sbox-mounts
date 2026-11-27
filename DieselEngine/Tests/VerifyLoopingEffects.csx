await Sandbox.Mounting.Directory.Mount( "payday" );
var results = new System.Collections.Generic.List<object>();
foreach ( var path in new[] { "effects/particles/equipment/vault_drill/drill_active", "effects/particles/environment/scene_sparks" } )
{
	var scene = new Scene();
	try
	{
		using var scope = scene.Push();
		scene.Enabled = true;
		var source = new GameObject( true, "Emitter source" );
		var go = new GameObject( false, "Looping effect" ) { NetworkMode = NetworkMode.Never };
		var effect = go.Components.Create( TypeLibrary.GetType( "Diesel.PaydayEffect" ) );
		var type = effect.GetType();
		type.GetProperty( "EffectPath" ).SetValue( effect, path );
		type.GetProperty( "DestroyWhenFinished" ).SetValue( effect, false );
		type.GetProperty( "Source" ).SetValue( effect, source );
		type.GetProperty( "SourceTransform" ).SetValue( effect, new Transform( Vector3.Up * 50 ) );
		go.Enabled = true;
		int Count( string name ) => (int)type.GetProperty( name ).GetValue( effect );
		Component[] Effects() => scene.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() ).Where( c => c.GetType().Name == "PaydayEffect" ).ToArray();
		async Task Tick( int count ) { for ( int i = 0; i < count; i++ ) { scene.GameTick( .05 ); await Task.Delay( 5 ); } }
		await Tick( 80 );
		int first = Count( "RespawnCount" ), firstChildren = Count( "ChildSpawnCount" );
		source.WorldPosition = new Vector3( 200, 80, 0 );
		await Tick( 180 );
		int respawns = Count( "RespawnCount" ), children = Count( "ChildSpawnCount" );
		if ( respawns <= first || respawns < 3 ) throw new Exception( $"Emitter did not keep recycling: {path}: {first}/{respawns}." );
		if ( go.WorldPosition.Distance( source.WorldPosition + Vector3.Up * 50 ) > .01f ) throw new Exception( "Emitter failed to follow its source." );
		if ( path.EndsWith( "scene_sparks" ) && (children <= firstChildren || children < 2) ) throw new Exception( "Recurring child sparks were not emitted." );
		if ( Effects().Length > 64 || Effects().Sum( e => (int)e.GetType().GetProperty( "ParticleCount" ).GetValue( e ) ) > 2048 )
			throw new Exception( "Scene effect budget exceeded." );
		var camera = new GameObject( true, "Camera" ).AddComponent<CameraComponent>();
		camera.WorldPosition = source.WorldPosition + new Vector3( 100, -130, 100 );
		camera.WorldRotation = Rotation.LookAt( go.WorldPosition - camera.WorldPosition );
		camera.BackgroundColor = new Color( .06f, .08f, .12f );
		if ( path.EndsWith( "scene_sparks" ) )
		{
			int previous = Count( "ChildSpawnCount" );
			for ( int i = 0; i < 120 && Count( "ChildSpawnCount" ) == previous; i++ ) await Tick( 1 );
			await Tick( 3 );
		}
		using ( var bitmap = new Bitmap( 900, 700 ) )
		{
			camera.RenderToBitmap( bitmap );
			System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"payday-loop-{path.Split('/').Last()}.png" ), bitmap.ToPng() );
		}
		// Explicit stop owns the whole child hierarchy; restarting reinitializes emission.
		type.GetMethod( "Stop" ).Invoke( effect, null );
		await Tick( 2 );
		if ( Count( "ParticleCount" ) != 0 || Effects().Length != 1 || scene.GetAllComponents<SpriteRenderer>().Any() )
			throw new Exception( "Stopped effect leaked children or sprites." );
		effect.Enabled = true;
		await Tick( 100 );
		if ( Count( "RespawnCount" ) == 0 ) throw new Exception( "Emitter did not restart." );
		source.Enabled = false;
		await Tick( 2 );
		if ( effect.Enabled || Effects().Length != 1 || scene.GetAllComponents<SpriteRenderer>().Any() )
			throw new Exception( "Disabled source left an active effect hierarchy." );
		source.Enabled = true; effect.Enabled = true;
		await Tick( 100 );
		source.Destroy();
		await Tick( 3 );
		if ( effect.Enabled || Count( "ParticleCount" ) != 0 || Effects().Length != 1 || scene.GetAllComponents<SpriteRenderer>().Any() )
			throw new Exception( "Destroyed source left an active effect hierarchy." );
		results.Add( new { Path = path, Respawns = respawns, ChildSpawns = children, Result = "PASS: recycling, source movement, stop/restart, budgets and source destruction." } );
	}
	finally { scene.Destroy(); }
}
return results;
