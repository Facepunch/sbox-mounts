using System.Globalization;
using System.Text;

namespace Diesel;

/// <summary>Diesel shares named intensity presets between lights and emissive materials.</summary>
static class LightIntensities
{
	public static Dictionary<string, float> Load( BundleDatabase database )
	{
		var bytes = database.Find( "light_intensities", "core/settings/light_intensities" );
		return bytes is null ? new() { ["identity"] = 1, ["none"] = 0 } : Read( bytes );
	}

	public static Dictionary<string, float> Read( byte[] bytes )
	{
		var result = new Dictionary<string, float>( StringComparer.Ordinal );
		foreach ( var element in XmlNode.Parse( Encoding.UTF8.GetString( bytes ).TrimEnd( '\0' ) ).Elements( "intensity" ) )
			if ( element.Attribute( "name" ) is string name
				&& float.TryParse( element.Attribute( "value" ), NumberStyles.Float, CultureInfo.InvariantCulture, out float value )
				&& float.IsFinite( value ) && value >= 0 ) result[name] = value;
		return result;
	}

	public static float Resolve( Dictionary<string, float> values, string name, float fallback = 1 )
	{
		if ( name is null ) return fallback;
		if ( values.TryGetValue( name, out float value ) ) return value;
		return float.TryParse( name, NumberStyles.Float, CultureInfo.InvariantCulture, out value )
			&& float.IsFinite( value ) && value >= 0 ? value : fallback;
	}
}
