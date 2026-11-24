namespace Diesel;

/// <summary>Initial environment values. Sun animation, baked ambient and transitions are not evaluated.</summary>
sealed record EnvironmentSettings( string Reflection, Vector3? Sun, Vector3? SkyTop, Vector3? SkyBottom )
{
	public bool HasSky => SkyTop.HasValue && SkyBottom.HasValue;

	public static EnvironmentSettings Read( BundleDatabase database, ScriptTable world )
	{
		string path = world.Table( "environment" )?.Table( "environment_values" )?.Text( "environment" );
		var bytes = string.IsNullOrEmpty( path ) ? null : database.Find( "environment", path );
		return FromData( bytes is null ? null : ScriptData.Read( bytes ).Table( "data" ) );
	}

	internal static EnvironmentSettings FromData( ScriptTable data )
	{
		var others = data?.Table( "others" );
		var ambient = data?.Table( "post_effect" )?.Table( "deferred" )?.Table( "deferred_lighting" )?.Table( "apply_ambient" );
		return new( Parameter( others, "global_texture" ) as string,
			ScaledColor( others, "sun_ray_color" ), ScaledColor( ambient, "sky_top_color" ), ScaledColor( ambient, "sky_bottom_color" ) );
	}

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
