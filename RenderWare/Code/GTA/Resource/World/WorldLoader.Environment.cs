using System;

namespace Renderware;

partial class WorldLoader
{
	/// <summary>
	/// Sun, ambient, sky and fog from one time cycle row - what the game would show at that hour and weather.
	/// </summary>
	void BuildEnvironment( in TimeCycle.Entry e, string weather )
	{
		var sunDirection = TimeCycle.SunDirection( Hour );
		var light = new GameObject( "light" ) { WorldRotation = Rotation.LookAt( -sunDirection ) };
		light.AddComponent<AmbientLight>().Color = e.Ambient;
		light.AddComponent<DirectionalLight>().LightColor = e.Directional;

		BuildSky( e, sunDirection, weather );

		// GTA's fog is linear from fog start to the far clip in the sky bottom colour, so the world fades into
		// the horizon exactly
		var fog = new GameObject( "effects" ).AddComponent<Fog>();
		fog.FogEnabled = true;
		fog.DistanceEnabled = true;
		fog.Mode = Sandbox.Rendering.FogMode.Distance;
		fog.DistanceRange = new Vector2( e.FogStart * Units.Scale, e.FarClip * Units.Scale );
		// GTA blended fog in gamma space (fixed function D3D), s&box blends in linear - a linear 25% towards white
		// reads about twice as strong. Raising the ramp to 2.2 gets the same perceived curve as the game.
		fog.FalloffExponent = 2.2f;
		fog.MaximumOpacity = 1;
		fog.HeightEnabled = false;
		fog.ColorSource = FogColorSource.Constant;
		// timecyc colours are gamma space; the fog takes linear (the sky shader converts the same way, so they match)
		fog.Color = new Color( MathF.Pow( e.SkyBottom.r, 2.2f ), MathF.Pow( e.SkyBottom.g, 2.2f ), MathF.Pow( e.SkyBottom.b, 2.2f ) );
		fog.SkyBlurDistanceEnabled = false;
		fog.SunLightingEnabled = false;
		fog.SkyAffect = 0; // the sky gradient already ends in the fog colour

		Log.Info( $"World environment: {weather} {Hour:0.##}h, fog {e.FogStart}-{e.FarClip} GTA units" );
	}

	/// <summary>
	/// The sky: a unit sphere the sky shader centres on the camera and draws behind everything, with the timecyc
	/// gradient, sun, moon, stars and clouds from the game's own sprites. Not a SkyBox2D, which mounted scenes can't use.
	/// </summary>
	void BuildSky( in TimeCycle.Entry e, Vector3 sunDirection, string weather )
	{
		// todo: time of day/weather component?

		bool Is( string word ) => weather.Contains( word, StringComparison.OrdinalIgnoreCase );

		Material skyMaterial = Material.Create( $"__{Host.Ident}_sky.vmat", "shaders/renderware_sky.shader", false );
		skyMaterial.Set( "g_vSkyTop", e.SkyTop );
		skyMaterial.Set( "g_vSkyBottom", e.SkyBottom );
		skyMaterial.Set( "g_vSunDirection", sunDirection );
		skyMaterial.Set( "g_vSunCore", e.SunCore );
		skyMaterial.Set( "g_vSunCorona", e.SunCorona );
		skyMaterial.Set( "g_flSunSize", e.SunSize );
		skyMaterial.Set( "g_flNight", StarVisibility( Hour ) );
		skyMaterial.Set( "g_vLowClouds", e.LowClouds );
		skyMaterial.Set( "g_vCloudTop", e.TopClouds );
		skyMaterial.Set( "g_vCloudBottom", e.BottomClouds );

		// the game thins the low clouds out in rain and fog. San Andreas' cloud sprites aren't the III / VC horizon
		// strips - drawn as a band they just tile into stripes - so it doesn't get one
		bool wet = Is( "RAIN" ) || Is( "FOG" ) || Is( "SANDSTORM" ) || Is( "HURRICANE" );
		skyMaterial.Set( "g_flLowCloudAlpha", Host.Game == GameVersion.SanAndreas ? 0f : wet ? 0.3f : 1.0f );
		skyMaterial.Set( "g_nFluffyClouds", Is( "RAIN" ) || Is( "HURRICANE" ) ? 24 : Is( "CLOUD" ) ? 16 : Is( "FOG" ) || Is( "SANDSTORM" ) ? 10 : 6 );

		// the game's own sky sprites (particle.txd); shader input -> texture
		foreach ( var (input, texture) in new[]
		{
			("Cloud1", "cloud1"), ("Cloud2", "cloud2"), ("Cloud3", "cloud3"),
			("CloudMasked", "cloudmasked"), ("CloudHilit", "cloudhilit"),
			("Corona", "corona"), ("Star", "coronastar"), ("Moon", "coronamoon"),
		} )
		{
			if ( Host.LoadTexture( Host.FindTexture( texture ) ) is { } tex )
				skyMaterial.Set( input, tex );
		}

		var renderer = new GameObject( true, "sky" ).AddComponent<SkyBox2D>();
		renderer.SkyMaterial = skyMaterial;
	}

	/// <summary>
	/// How visible stars and the moon are: full from 23:00 to 4:00, fading over the hour either side.
	/// </summary>
	static float StarVisibility( float hour )
	{
		hour = ((hour % 24) + 24) % 24;
		if ( hour >= 23 || hour < 4 ) return 1;
		if ( hour >= 22 ) return hour - 22;
		if ( hour < 5 ) return 5 - hour;
		return 0;
	}
}
