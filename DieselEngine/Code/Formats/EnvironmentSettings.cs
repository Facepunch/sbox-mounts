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
		};
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
