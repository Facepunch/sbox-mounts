namespace Diesel;

/// <summary>Initial environment values. Baked ambient and transitions are not evaluated.</summary>
sealed record EnvironmentSettings( string Reflection, Vector3? Sun, Vector3? SkyTop, Vector3? SkyBottom )
{
	public bool HasSky => SkyTop.HasValue && SkyBottom.HasValue;
	/// <summary>Sky underlay scene/model, e.g. <c>core/environments/skies/default/default</c>.</summary>
	public string Underlay { get; init; }
	/// <summary><c>others/sun_anim</c>: normalized time of the underlay's <c>sun_vertical</c> group.</summary>
	public float? SunVertical { get; init; }
	/// <summary><c>others/sun_anim_x</c>: normalized time of the underlay's <c>sun_horizontal</c> group.</summary>
	public float? SunHorizontal { get; init; }
	/// <summary>World <c>environment_values/sky_rot</c> in degrees.</summary>
	public float SkyRotation { get; init; }
	/// <summary>Unit direction towards the sun in s&amp;box axes, evaluated from the underlay rig.</summary>
	public Vector3? SunDirection { get; init; }
	/// <summary><c>post_effect/fog_processor/fog/fog</c>, or null when absent/invisible.</summary>
	public FogSettings Fog { get; init; }
	/// <summary>
	/// <c>underlay_effect/sky/color0–2 × scale</c>: the underlay dome's zenith, middle and horizon colours,
	/// or null if any is missing. (<c>g_sky</c>'s V coordinate runs from the horizon at 0 to the zenith at −1.)
	/// </summary>
	public Vector3[] SkyGradient { get; init; }

	/// <summary>
	/// Diesel's distance fog. <c>start</c>/<c>end1</c> are fractions of the fog range, <c>alpha1</c> is the
	/// far opacity and <c>start_color × start_color_scale</c> the colour (the <c>color0–2</c> layers are unused in
	/// shipped PDTH environments: their scales are 0 except one debug-red Heat Street layer).
	/// </summary>
	public sealed record FogSettings( Vector3 Color, float Start, float End, float NearOpacity, float FarOpacity );

	public static EnvironmentSettings Read( BundleDatabase database, ScriptTable world )
	{
		var values = world.Table( "environment" )?.Table( "environment_values" );
		string path = values?.Text( "environment" );
		var bytes = string.IsNullOrEmpty( path ) ? null : database.Find( "environment", path );
		var settings = FromData( bytes is null ? null : ScriptData.Read( bytes ).Table( "data" ) );
		settings = settings with { SkyRotation = values?.GetValueOrDefault( "sky_rot" ) is float rot && float.IsFinite( rot ) ? rot : 0 };
		if ( settings.SunVertical is { } vertical && settings.SunHorizontal is { } horizontal )
		{
			try { settings = settings with { SunDirection = Diesel.SunDirection.Evaluate( database, settings.Underlay, vertical, horizontal, settings.SkyRotation ) }; }
			catch ( InvalidDataException ) { }
		}
		return settings;
	}

	internal static EnvironmentSettings FromData( ScriptTable data )
	{
		var others = data?.Table( "others" );
		var ambient = data?.Table( "post_effect" )?.Table( "deferred" )?.Table( "deferred_lighting" )?.Table( "apply_ambient" );
		return new( Parameter( others, "global_texture" ) as string,
			ScaledColor( others, "sun_ray_color" ), ScaledColor( ambient, "sky_top_color" ), ScaledColor( ambient, "sky_bottom_color" ) )
		{
			Underlay = Parameter( others, "underlay" ) as string,
			SunVertical = Fraction( others, "sun_anim" ),
			SunHorizontal = Fraction( others, "sun_anim_x" ),
			Fog = ReadFog( data?.Table( "post_effect" )?.Table( "fog_processor" )?.Table( "fog" )?.Table( "fog" ) ),
			SkyGradient = ReadSkyGradient( data?.Table( "underlay_effect" )?.Table( "sky" ) ),
		};
	}

	static Vector3[] ReadSkyGradient( ScriptTable sky )
	{
		var colors = new[] { "color0", "color1", "color2" }.Select( c => ScaledColor( sky, c ) ).ToArray();
		return colors.All( c => c.HasValue ) ? colors.Select( c => c.Value ).ToArray() : null;
	}

	/// <summary>Sky colour at a height on the dome: 0 = horizon (color2), 0.5 = color1, 1 = zenith (color0).</summary>
	public static Vector3 SampleSky( Vector3[] gradient, float height )
	{
		float t = Math.Clamp( height, 0, 1 );
		return t < 0.5f ? Vector3.Lerp( gradient[2], gradient[1], t * 2 ) : Vector3.Lerp( gradient[1], gradient[0], (t - 0.5f) * 2 );
	}

	static FogSettings ReadFog( ScriptTable fog )
	{
		if ( ScaledColor( fog, "start_color" ) is not { } color ) return null;
		if ( Fraction( fog, "start" ) is not { } start || Fraction( fog, "end1" ) is not { } end || Fraction( fog, "alpha1" ) is not { } far ) return null;
		float near = Fraction( fog, "alpha0" ) ?? 0;
		// No Mercy authors alpha1 = 0 with a zero colour scale: no fog at all.
		if ( far <= 0 || end <= start || color == Vector3.Zero ) return null;
		return new( color, start, end, near, far );
	}

	static float? Fraction( ScriptTable table, string name ) =>
		Parameter( table, name ) is float value && float.IsFinite( value ) ? Math.Clamp( value, 0, 1 ) : null;

	static object Parameter( ScriptTable table, string key ) =>
		table?.Children.FirstOrDefault( p => p.Meta == "param" && p.Text( "key" ) == key )?.GetValueOrDefault( "value" );

	static Vector3? ScaledColor( ScriptTable table, string name )
	{
		if ( Parameter( table, name ) is not Vector3 color || Parameter( table, name + "_scale" ) is not float scale
			|| !float.IsFinite( color.x + color.y + color.z + scale ) || scale < 0 || color.x < 0 || color.y < 0 || color.z < 0 )
			return null;
		color *= scale;
		return float.IsFinite( color.x + color.y + color.z ) ? color : null;
	}
}
