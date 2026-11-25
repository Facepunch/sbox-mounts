namespace Diesel;

static class MapEnvironment
{
	/// <summary>
	/// Distance the normalized Diesel fog fractions are scaled by, in centimetres. Not authored per map; the
	/// fractions are assumed to be depth over the camera far range, taken as 2.5 km (unconfirmed for PDTH). The first guess,
	/// 500 m (from the 500 m shadow fade-out), visibly fogged trees ~30 m away on Bank, and the original game doesn't.
	/// This is a calibration constant: compare distant haze against the original game and adjust.
	/// </summary>
	const float FogRange = 250000;

	public static void Add( GameObject root, EnvironmentSettings environment )
	{
		AddFog( root, environment?.Fog );
		// The authored direction comes from the underlay's d_sun node (see SunDirection). A light
		// shines along its forward axis, i.e. from the sun towards the world. Keep the old preview
		// angle only when the environment or underlay couldn't be evaluated, or the sun is below the horizon.
		var towardsSun = environment?.SunDirection;
		bool authored = towardsSun is { } d && d.z > 0.01f;
		var sun = new GameObject( true, authored ? "Sunlight" : "Sunlight (preview direction)" )
		{
			Parent = root,
			WorldRotation = authored ? Rotation.LookAt( -towardsSun.Value ) : Rotation.From( 55, 30, 0 ),
		};
		var color = environment?.Sun ?? new Vector3( 0.7f, 0.7f, 0.7f );
		sun.AddComponent<DirectionalLight>().LightColor = new Color( color.x, color.y, color.z );
		// Valid hemispherical sky lighting is evaluated per surface normal in the Diesel shader, fed
		// scene-wide so spawned props/NPCs/weapons match. An extra ambient component would double-light them.
		if ( environment?.HasSky == true )
		{
			var lighting = root.AddComponent<PaydayEnvironmentLighting>();
			lighting.SkyTop = environment.SkyTop.Value;
			lighting.SkyBottom = environment.SkyBottom.Value;
		}
		else
			new GameObject( true, "Preview ambient (missing environment)" ) { Parent = root }
				.AddComponent<AmbientLight>().Color = Color.White * 0.3f;
	}

	static void AddFog( GameObject root, EnvironmentSettings.FogSettings settings )
	{
		if ( settings is null ) return;
		var go = new GameObject( true, "Fog" ) { Parent = root };
		go.AddComponent<PostProcessVolume>().SceneVolume = new Sandbox.Volumes.SceneVolume { Type = Sandbox.Volumes.SceneVolume.VolumeTypes.Infinite };
		var fog = go.AddComponent<Fog>();
		fog.Mode = Sandbox.Rendering.FogMode.Distance;
		fog.DistanceRange = new Vector2( settings.Start, settings.End ) * FogRange * Coordinates.Scale;
		fog.FalloffExponent = 1;
		fog.MaximumOpacity = settings.FarOpacity;
		fog.ColorSource = FogColorSource.Constant;
		fog.Color = new Color( settings.Color.x, settings.Color.y, settings.Color.z );
		fog.HeightEnabled = false;
		fog.SunLightingEnabled = false;
		// Diesel draws the sky underlay separately from the world's fog; keep the sky clear.
		fog.SkyAffect = 0;
	}
}
