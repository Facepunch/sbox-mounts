using System;

namespace GoldSrc;

/// <summary>
/// titles.txt, the game's table of on-screen messages - env_message names an entry here rather
/// than carrying its own text.
///
/// A "$" directive sets a display parameter for every message declared after it until it's
/// changed again, and a message is a name on its own line followed by its text in braces.
/// </summary>
internal static class Titles
{
	/// <summary>
	/// Display settings for a missing title, showing the name itself as the text.
	/// </summary>
	public static TitleMessage Fallback => new()
	{
		Effect = 2,
		Color = FromBytes( 100, 100, 100 ),
		Color2 = FromBytes( 240, 110, 0 ),
		Position = new Vector2( -1f, 0.7f ),
		FadeIn = 0.01f,
		FadeOut = 1.5f,
		FxTime = 0.25f,
		HoldTime = 5f
	};

	public static Dictionary<string, TitleMessage> Parse( string contents )
	{
		var messages = new Dictionary<string, TitleMessage>( StringComparer.OrdinalIgnoreCase );
		var lines = contents.Split( '\n' );
		var current = Fallback;

		for ( var i = 0; i < lines.Length; i++ )
		{
			var line = lines[i].Trim();

			if ( line.Length == 0 || line.StartsWith( "//", StringComparison.Ordinal ) )
				continue;

			if ( line[0] == '$' )
			{
				ApplyDirective( ref current, line );
				continue;
			}

			// A name only counts as one if its text block follows.
			if ( i + 1 >= lines.Length || lines[i + 1].Trim() != "{" )
				continue;

			var body = new List<string>();
			for ( i += 2; i < lines.Length && lines[i].Trim() != "}"; i++ )
				body.Add( lines[i].TrimEnd() );

			current.Text = string.Join( '\n', body ).Trim( '\n' );
			messages[line] = current;
		}

		return messages;
	}

	private static void ApplyDirective( ref TitleMessage message, string line )
	{
		var split = line.IndexOfAny( Whitespace );
		if ( split < 0 )
			return;

		var value = line[split..].Split( "//" )[0].Trim();

		switch ( line[1..split].ToLowerInvariant() )
		{
			case "position": message.Position = ParsePosition( value ); break;
			case "effect": message.Effect = value.ToInt(); break;
			case "color": message.Color = ParseColor( value ); break;
			case "color2": message.Color2 = ParseColor( value ); break;
			case "fadein": message.FadeIn = value.ToFloat(); break;
			case "fadeout": message.FadeOut = value.ToFloat(); break;
			case "fxtime": message.FxTime = value.ToFloat(); break;
			case "holdtime": message.HoldTime = value.ToFloat(); break;
		}
	}

	private static Vector2 ParsePosition( string value )
	{
		var n = Numbers( value );
		return n.Length >= 2 ? new Vector2( n[0], n[1] ) : Vector2.Zero;
	}

	private static Color ParseColor( string value )
	{
		var n = Numbers( value );
		return n.Length >= 3 ? FromBytes( n[0], n[1], n[2] ) : Color.White;
	}

	private static Color FromBytes( float r, float g, float b ) => new( r / 255f, g / 255f, b / 255f );

	private static float[] Numbers( string value )
		=> value.Split( Whitespace, StringSplitOptions.RemoveEmptyEntries ).Select( x => x.ToFloat() ).ToArray();

	private static readonly char[] Whitespace = [' ', '\t'];
}
