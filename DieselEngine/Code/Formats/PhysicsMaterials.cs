using System.Globalization;
using System.Text;

namespace Diesel;

/// <summary>Diesel physics material densities, authored in kg/m³.</summary>
static class PhysicsMaterials
{
	public static Dictionary<string, float> Read( byte[] bytes )
	{
		var result = new Dictionary<string, float>( StringComparer.Ordinal );
		if ( bytes is null ) return result;
		var root = XmlNode.Parse( Encoding.UTF8.GetString( bytes ).TrimEnd( '\0' ) );
		foreach ( var material in root.Elements( "material" ) )
			if ( material.Attribute( "name" ) is { Length: > 0 } name
				&& Positive( material.Attribute( "density" ) ) is float density )
				result[name] = density;
		return result;
	}

	public static float? Positive( string value ) =>
		float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number )
		&& float.IsFinite( number ) && number > 0 ? number : null;
}
