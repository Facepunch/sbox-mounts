// Full Bank scene, disposable and local. Measures bounded simulation, not a GPU benchmark.
await Sandbox.Mounting.Directory.Mount( "payday" );
var file = await ResourceLibrary.LoadAsync<SceneFile>( "mount://payday/levels/bank/world.world.scene" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	if ( file is null || !scene.Load( file ) ) throw new Exception( "Bank scene failed to load." );
	scene.Enabled = true;
	var fixtures = new System.Collections.Generic.List<GameObject>();
	var type = TypeLibrary.GetType( "Diesel.PaydayEffect" );
	for ( int i = 0; i < 32; i++ )
	{
		var go = new GameObject( false, $"Stress emitter {i}" ) { NetworkMode = NetworkMode.Never };
		var effect = go.Components.Create( type );
		effect.GetType().GetProperty( "EffectPath" ).SetValue( effect, "effects/particles/window/bank_window_vault" );
		go.WorldPosition = new Vector3( (i % 8) * 100, (i / 8) * 100, 200 );
		fixtures.Add( go ); go.Enabled = true;
	}
	int peak = 0;
	double maxTickMs = 0;
	var timer = new System.Diagnostics.Stopwatch();
	for ( int i = 0; i < 300; i++ )
	{
		timer.Restart(); scene.GameTick( .05 ); timer.Stop();
		maxTickMs = Math.Max( maxTickMs, timer.Elapsed.TotalMilliseconds );
		var effects = scene.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() ).Where( c => c.GetType().Name == "PaydayEffect" ).ToArray();
		int count = effects.Sum( e => (int)e.GetType().GetProperty( "ParticleCount" ).GetValue( e ) );
		peak = Math.Max( peak, count );
		if ( count > 2048 ) throw new Exception( $"Scene particle cap exceeded: {count}" );
		await Task.Delay( 5, cancellationToken );
	}
	if ( peak == 0 ) throw new Exception( "Stress fixtures never produced particles." );
	if ( fixtures.Any( g => g.IsValid() ) ) throw new Exception( "Finite stress emitters did not clean up." );
	return new { Map = "Bank", Models = scene.GetAllComponents<ModelRenderer>().Count(),
		Emitters = fixtures.Count, PeakParticles = peak, MaxSimulationTickMs = maxTickMs, Result = "PASS" };
}
finally { scene.Destroy(); }
