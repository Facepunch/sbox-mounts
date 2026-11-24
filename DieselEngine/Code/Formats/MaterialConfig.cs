using System.Globalization;
using System.Text;

namespace Diesel;

/// <summary>Material names match the hashed material sections referenced by model render atoms.</summary>
sealed record MaterialDefinition( string Name, string Template, string Diffuse, string Opacity, float AlphaReference, string Normal )
{
	public bool AlphaTest => Template.Split( ':' ).Contains( "ALPHA_MASKED" );
	public bool Translucent => Effect || Template.StartsWith( "opacity:", StringComparison.Ordinal ) || Template.StartsWith( "decal:", StringComparison.Ordinal );
	public bool Effect => Template.StartsWith( "effect:", StringComparison.Ordinal );
	public int EffectBlend => !Effect ? 0 : Template.Split( ':' ).Contains( "BLEND_ADD" ) ? 1 : Template.Split( ':' ).Contains( "BLEND_MUL" ) ? 2 : 0;
	public int DetailBlend => !Effect ? 0 : Template.Split( ':' ).Contains( "DIFFUSE1_BLEND_OP_RGB_ADD" ) ? 1 : Template.Split( ':' ).Contains( "DIFFUSE1_BLEND_OP_RGB_MUL" ) ? 2 : 0;
	public string Detail { get; init; }
	public string EffectIntensity { get; init; }
	public bool ViewAngleFalloff => Effect && Template.Split( ':' ).Contains( "VIEW_ANGLE_FALLOFF_OP" );
	public bool BlueMaskTint => Template.Split( ':' ).Contains( "BLUE_MASK_TINT" );
	public Vector3 TintColor { get; init; } = Vector3.One;
	public float FalloffExponent { get; init; } = 1;
	public Vector2 UV0Speed { get; init; }
	public Vector2 UV1Speed { get; init; }
	public Vector2 DetailScroll => Effect && Template.Split( ':' ).Contains( "DIFFUSE1_UVANIM" ) ? UV1Speed : Vector2.Zero;
	public bool Hidden => Template.StartsWith( "shadow_caster_only", StringComparison.Ordinal );
	public bool VertexColor => Template.Split( ':' ).Contains( "VERTEX_COLOR" );
	public bool DoubleSided => Template.Split( ':' ).Contains( "DOUBLE_SIDED" );
	public bool Backdrop => Template.Split( ':' ).Contains( "RL_BACKDROP" );
	public bool SelfIlluminated => Template.Split( ':' ).Contains( "SELF_ILLUMINATION" );
	public string Emission { get; init; }
	public string EmissionMultiplier { get; init; }
	public Vector2 UVSpeed { get; init; }
	public Vector2 DiffuseScroll => Effect ? (Template.Split( ':' ).Contains( "DIFFUSE0_UVANIM" ) ? UV0Speed : Vector2.Zero) : Template.Split( ':' ).Contains( "DIFFUSE_UVANIM" ) ? UVSpeed : Vector2.Zero;
	public Vector2 NormalScroll => Template.Split( ':' ).Contains( "NORMAL_UVANIM" ) ? UVSpeed : Vector2.Zero;
	public Vector2 EmissionScroll => Template.Split( ':' ).Contains( "SELF_ILLUMINATION_UVANIM" ) ? UVSpeed : Vector2.Zero;
}

static class MaterialConfig
{
	public static Dictionary<ulong, MaterialDefinition> Read( byte[] bytes )
	{
		var root = XmlNode.Parse( Encoding.UTF8.GetString( bytes ).TrimEnd( '\0' ) );
		var result = new Dictionary<ulong, MaterialDefinition>();
		foreach ( var material in root.Elements( "material" ) )
		{
			string name = material.Attribute( "name" );
			if ( string.IsNullOrEmpty( name ) ) continue;
			string alpha = material.Elements( "variable" ).FirstOrDefault( v => v.Attribute( "name" ) == "alpha_ref" )?.Attribute( "value" );
			float alphaRef = float.TryParse( alpha, NumberStyles.Float, CultureInfo.InvariantCulture, out var a ) && float.IsFinite( a ) ? Math.Clamp( a, 0, 1 ) : 0.5f;
			result[IdString.Hash( name )] = new( name, material.Attribute( "render_template" ) ?? "",
				material.Element( "diffuse_texture" )?.Attribute( "file" ),
				material.Element( "opacity_texture" )?.Attribute( "file" ), alphaRef,
				material.Element( "bump_normal_texture" )?.Attribute( "file" ) )
			{
				Detail = material.Element( "detail_diffuse_texture" )?.Attribute( "file" ),
				EffectIntensity = material.Elements( "variable" ).FirstOrDefault( v => v.Attribute( "name" ) == "intensity" )?.Attribute( "value" ),
				FalloffExponent = ReadNonnegative( material.Elements( "variable" ).FirstOrDefault( v => v.Attribute( "name" ) == "falloff_exp" )?.Attribute( "value" ), 1 ),
				TintColor = ReadColor( material.Elements( "variable" ).FirstOrDefault( v => v.Attribute( "name" ) == "tint_color" )?.Attribute( "value" ) ),
				UV0Speed = ReadScroll( material.Elements( "variable" ).FirstOrDefault( v => v.Attribute( "name" ) == "uv0_speed" )?.Attribute( "value" ) ),
				UV1Speed = ReadScroll( material.Elements( "variable" ).FirstOrDefault( v => v.Attribute( "name" ) == "uv1_speed" )?.Attribute( "value" ) ),
				Emission = material.Element( "self_illumination_texture" )?.Attribute( "file" ),
				EmissionMultiplier = material.Elements( "variable" ).FirstOrDefault( v => v.Attribute( "name" ) == "il_multiplier" )?.Attribute( "value" ),
				UVSpeed = ReadScroll( material.Elements( "variable" ).FirstOrDefault( v => v.Attribute( "name" ) == "uv_speed" )?.Attribute( "value" ) )
			};
		}
		return result;
	}

	static Vector2 ReadScroll( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) ) return Vector2.Zero;
		var parts = value.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
		if ( parts.Length != 3
			|| !float.TryParse( parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float u )
			|| !float.TryParse( parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v )
			|| !float.IsFinite( u ) || !float.IsFinite( v ) ) return Vector2.Zero;
		// The third component is not a translation speed; rotation/stepping modes remain unsupported.
		return new( u, v );
	}

	static float ReadNonnegative( string text, float fallback ) =>
		float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value )
			&& float.IsFinite( value ) && value >= 0 ? value : fallback;

	static Vector3 ReadColor( string text )
	{
		var parts = text?.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
		if ( parts?.Length != 3 ) return Vector3.One;
		float r = ReadNonnegative( parts[0], float.NaN ), g = ReadNonnegative( parts[1], float.NaN ), b = ReadNonnegative( parts[2], float.NaN );
		return float.IsFinite( r + g + b ) ? new Vector3( r, g, b ) : Vector3.One;
	}
}
