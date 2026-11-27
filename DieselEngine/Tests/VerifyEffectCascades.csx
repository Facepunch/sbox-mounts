await Sandbox.Mounting.Directory.Mount( "payday" );
var results = new System.Collections.Generic.List<object>();
foreach ( var path in new[] { "effects/particles/explosions/apartment_detail_explosion", "effects/particles/fire/burning_dude" } )
{
	var scene = new Scene();
	try
	{
		using var scope = scene.Push();
		scene.Enabled = true;
		var root = new GameObject( false, "Finite effect" ) { NetworkMode = NetworkMode.Never };
		var effect = root.Components.Create( TypeLibrary.GetType( "Diesel.PaydayEffect" ) );
		var type = effect.GetType();
		type.GetProperty( "EffectPath" ).SetValue( effect, path );
		root.Enabled = true;
		int peakEffects = 0, peakParticles = 0, childSpawns = 0, respawns = 0;
		bool childrenOutlivedParticles = false;
		for ( int i = 0; i < 700; i++ )
		{
			scene.GameTick( .05 ); await Task.Delay( 5 );
			var all = scene.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() ).Where( c => c.GetType().Name == "PaydayEffect" ).ToArray();
			peakEffects = Math.Max( peakEffects, all.Length );
			peakParticles = Math.Max( peakParticles, all.Sum( e => (int)type.GetProperty( "ParticleCount" ).GetValue( e ) ) );
			if ( effect.IsValid() )
			{
				childSpawns = (int)type.GetProperty( "ChildSpawnCount" ).GetValue( effect );
				respawns = (int)type.GetProperty( "RespawnCount" ).GetValue( effect );
				if ( (int)type.GetProperty( "ParticleCount" ).GetValue( effect ) == 0 && all.Length > 1 ) childrenOutlivedParticles = true;
			}
			if ( all.Length == 0 ) break;
		}
		if ( root.IsValid() || scene.GetAllComponents<SpriteRenderer>().Any() ) throw new Exception( $"Finite effect leaked: {path}" );
		if ( peakEffects > 64 || peakParticles > 2048 ) throw new Exception( "Effect budget exceeded." );
		if ( path.Contains( "apartment_detail" ) && childSpawns == 0 ) throw new Exception( "Tick-driven children were not emitted." );
		if ( path.Contains( "burning_dude" ) && respawns == 0 ) throw new Exception( "Frequency-driven particles did not recycle." );
		results.Add( new { Path = path, ChildSpawns = childSpawns, Respawns = respawns, PeakEffects = peakEffects,
			PeakParticles = peakParticles, ChildrenOutlivedParticles = childrenOutlivedParticles, Result = "PASS: emission and finite hierarchy cleanup." } );
	}
	finally { scene.Destroy(); }
}
return results;
