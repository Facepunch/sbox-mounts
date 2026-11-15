using System;

namespace Source1;

/// <summary>
/// Reads Counter-Strike's BotChatter.db: each Chatter phrase is a list of waves a bot says one of. A Count or Place line qualifies
/// the waves after it, which are then only said of that many enemies or at that place. Radio names the standard radio event said
/// instead when bots may only use the radio; Important phrases are still spoken at minimal chatter. Place phrases (location
/// callouts) are left out.
/// </summary>
internal static class SourceBotChatter
{
	/// <summary>A wave and what it is said of: an enemy count (1, 2, 3 or Many) and a place (ANY, UNDEFINED or a place name).</summary>
	internal sealed record Line( string Wave, string Count, string Place );
	internal sealed record Phrase( string Radio, bool Important, Line[] Lines );

	/// <summary>Chatter phrases by name; waves that do not exist are left out, and phrases left without any.</summary>
	internal static Dictionary<string, Phrase> Parse( string text, Func<string, bool> exists )
	{
		var tokens = new List<string>();
		foreach ( var line in text.Split( '\n' ) )
		{
			var comment = line.IndexOf( "//", StringComparison.Ordinal );
			tokens.AddRange( (comment >= 0 ? line[..comment] : line).Split( [' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries ) );
		}

		var i = 0;
		string Next() => i + 1 < tokens.Count ? tokens[++i] : null;
		bool Is( string keyword ) => tokens[i].Equals( keyword, StringComparison.OrdinalIgnoreCase );

		var folder = "";
		var phrases = new Dictionary<string, Phrase>( StringComparer.OrdinalIgnoreCase );
		for ( ; i < tokens.Count; i++ )
		{
			if ( Is( "BaseDir" ) )
			{
				var directory = Next()?.Replace( '\\', '/' ).Trim( '/' ).ToLowerInvariant();
				folder = string.IsNullOrEmpty( directory ) ? "" : directory + "/";
				continue;
			}

			var chatter = Is( "Chatter" );
			if ( !chatter && !Is( "Place" ) || Next() is not { } name )
				continue;

			string radio = null;
			string count = null;
			string place = null;
			var important = false;
			var lines = new List<Line>();
			while ( ++i < tokens.Count && !Is( "End" ) )
			{
				if ( Is( "Radio" ) )
					radio = Next();
				else if ( Is( "Count" ) )
					count = Next();
				else if ( Is( "Place" ) )
					place = Next();
				else if ( Is( "Important" ) )
					important = true;
				else if ( $"sound/{folder}{tokens[i].Replace( '\\', '/' ).ToLowerInvariant()}" is var wave && exists( wave ) )
					lines.Add( new Line( wave, count, place ) );
			}

			if ( chatter && lines.Count > 0 )
				phrases.TryAdd( name, new Phrase( radio, important, [.. lines] ) );
		}

		return phrases;
	}
}
