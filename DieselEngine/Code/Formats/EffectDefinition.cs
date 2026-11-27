using System.Globalization;
using System.Text;

namespace Diesel;

/// <summary>Original Diesel particle stacks. Unknown operators stay in the source and are reported.</summary>
sealed class EffectDefinition
{
	public XmlNode Root { get; }
	public XmlNode[] Atoms { get; }
	public string[] Textures { get; }
	public string[] Unsupported { get; }
	public string[] ChildEffects { get; }
	public bool IsLooping => Atoms.Any( a => Number( a, "lifetime", 1 ) < 0 );
	public bool HasPlayback => ChildEffects.Length > 0 || Atoms.Any( a => a.Element( "visualizerstack" )?.Elements( "billboard" ).Any() == true );
	public string Source { get; }
	public EffectDefinition( byte[] bytes )
	{
		Source = Encoding.UTF8.GetString( bytes );
		Root = XmlNode.Parse( Source );
		if ( Root.Name != "effect" ) throw new InvalidDataException( "Expected a Diesel effect." );
		Atoms = Root.Elements( "atom" ).ToArray();
		if ( Atoms.Length > 256 ) throw new InvalidDataException( "Too many effect atoms." );
		Textures = Atoms.SelectMany( a => a.Element( "visualizerstack" )?.Children ?? [] )
			.Select( n => n.Attribute( "texture" ) ).Where( n => !string.IsNullOrEmpty( n ) ).Distinct().ToArray();
		ChildEffects = Atoms.SelectMany( a => a.Elements( "effect_spawn" ) ).Select( s => s.Attribute( "effect" ) )
			.Where( p => !string.IsNullOrEmpty( p ) ).Distinct().ToArray();
		Unsupported = Atoms.SelectMany( a => a.Children.Where( n => n.Name.EndsWith( "stack" ) )
			.SelectMany( n => n.Children ) ).Where( n => !Supported.Contains( n.Name ) )
			.Select( n => n.Name ).Distinct().Order().ToArray();
	}
	static readonly HashSet<string> Supported = [
		"boxrandomposition", "localboxrandomposition", "boxrandomvelocity", "boxrandomaspectsize",
		"boxrandomsize", "boxrandomangle", "boxrandomanglevelocity", "boxrandomage", "constantage",
		"boxrandomuvoffset", "constantacceleration", "velocityintegrator", "scaledvelocityintegrator",
		"anglevelocityintegrator", "ager", "worldtransform", "billboard", "teleporter", "eventtick",
		"boxrandomopacity", "boxrandomcolor", "boxrandomcolorgradient"
	];
	public static float Number( XmlNode n, string key, float fallback = 0 )
	{
		if ( n?.Attribute( key ) is not { } text ) return fallback;
		return float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) && float.IsFinite( value )
			? value : throw new InvalidDataException( $"Invalid effect number {key}={text}." );
	}
	public static Vector3 Vector( XmlNode n, string key, Vector3 fallback = default )
	{
		if ( n?.Attribute( key ) is not { } text ) return fallback;
		var parts = text.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		if ( parts.Length is < 1 or > 3 ) throw new InvalidDataException( $"Invalid effect vector {text}." );
		var values = parts.Select( p => float.Parse( p, CultureInfo.InvariantCulture ) ).ToArray();
		if ( values.Any( v => !float.IsFinite( v ) ) ) throw new InvalidDataException( "Nonfinite effect vector." );
		return new Vector3( values[0], values.Length > 1 ? values[1] : values[0], values.Length > 2 ? values[2] : values[0] );
	}
	public static Vector3 Curve( XmlNode node, string name, float time, Vector3 fallback )
	{
		var curve = node?.Element( name );
		if ( curve is null ) return fallback;
		var keys = curve.Elements( "key" ).ToArray();
		if ( keys.Length == 0 ) return fallback;
		float end = Number( keys[^1], "t" );
		if ( curve.Attribute( "loop" ) == "true" && end > 0 ) time %= end;
		var before = keys[0];
		foreach ( var after in keys.Skip( 1 ) )
		{
			float t = Number( after, "t" );
			if ( t > time )
			{
				float start = Number( before, "t" );
				return Vector3.Lerp( Vector( before, "v" ), Vector( after, "v" ),
					Math.Clamp( (time - start) / Math.Max( .00001f, t - start ), 0, 1 ) );
			}
			before = after;
		}
		return Vector( before, "v" );
	}
}
