var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
var prefabs = mount.GetAll( Sandbox.Mounting.ResourceType.PrefabFile )
	.Where( r => r.Path.Contains( "/entities/effects/" ) || r.Path.Contains( "/entities/debris/" ) ).ToArray();
if ( prefabs.Length == 0 ) throw new Exception( "No effect/debris browser prefabs." );
foreach ( var prefab in prefabs )
	if ( await ResourceLibrary.LoadAsync<PrefabFile>( prefab.Path ) is null ) throw new Exception( $"Missing {prefab.Path}" );
var definitions = mount.GetAll( Sandbox.Mounting.ResourceType.GameResource ).Where( r => r.Path.EndsWith( ".pdeffect" ) ).ToArray();
foreach ( var definition in definitions )
{
	var resource = await ResourceLibrary.LoadAsync<GameResource>( definition.Path );
	if ( resource is null || !((string)resource.GetType().GetProperty( "SourceXml" ).GetValue( resource )).Contains( "<effect" ) )
		throw new Exception( $"Missing source data: {definition.Path}" );
}
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	const string effectPath = "mount://payday/entities/effects/particles/window/bank_window_vault.prefab";
	const string debrisPath = "mount://payday/entities/debris/units/world/architecture/secret_stash/props/planks_debris.prefab";
	var spawnedEffect = GameObject.GetPrefab( effectPath ).Clone( Vector3.Up * 120 );
	var initialEffect = spawnedEffect.Components.GetAll().FirstOrDefault( c => c.GetType().Name == "PaydayEffect" );
	if ( initialEffect is null ) throw new Exception( "Effect prefab is missing its playback component." );
	// Keep the test emitter available while first-use texture uploads complete.
	initialEffect.GetType().GetProperty( "DestroyWhenFinished" ).SetValue( initialEffect, false );
	GameObject.GetPrefab( debrisPath ).Clone( Vector3.Up * 200 );
	int peakParticles = 0;
	for ( int i = 0; i < 100 && peakParticles == 0; i++ )
	{
		scene.GameTick( .02 ); await Task.Delay( 10 );
		peakParticles = Math.Max( peakParticles, (int)initialEffect.GetType().GetProperty( "ParticleCount" ).GetValue( initialEffect ) );
	}
	var all = scene.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() ).ToArray();
	var effects = all.Where( c => c.GetType().Name == "PaydayEffect" ).ToArray();
	var debris = all.Count( c => c.GetType().Name == "PaydayDebris" );
	if ( effects.Length != 1 || peakParticles == 0 || debris != 5 )
		throw new Exception( $"Standalone effect/debris prefab failed: effects={effects.Length}, particles={(effects.Length == 0 ? -1 : (int)effects[0].GetType().GetProperty("ParticleCount").GetValue(effects[0]))}, debris={debris}." );
	return new { Definitions = definitions.Length, EffectPrefabs = prefabs.Count( p => p.Path.Contains( "/entities/effects/" ) ),
		DebrisPrefabs = prefabs.Count( p => p.Path.Contains( "/entities/debris/" ) ), SpawnedFragments = debris, Result = "PASS" };
}
finally { scene.Destroy(); }
