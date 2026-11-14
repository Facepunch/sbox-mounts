using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Source1;

/// <summary>
/// Reads scripts/sentences.txt: each sentence is a folder and a list of wave words played back to back. Sentences
/// numbered alike (COMBINE_ALERT0, COMBINE_ALERT1) form one group of alternatives.
/// </summary>
internal static class SourceSentences
{
	/// <summary>One spoken word. A virtual word (V_MYNAMES) lists every wave it may be; <see cref="Speaker"/> words stay the same per speaker.</summary>
	internal sealed record Word( string[] Waves, float Pitch, float Volume, bool Speaker );

	/// <summary>Sentence groups, each a list of alternatives, each an ordered list of words. Waves that do not exist are left out.</summary>
	internal static Dictionary<string, List<Word[]>> Parse( string text, Func<string, bool> exists )
	{
		var raw = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var line in text.Split( '\n' ) )
		{
			var content = line;
			var comment = content.IndexOf( "//", StringComparison.Ordinal );
			if ( comment >= 0 )
				content = content[..comment];

			var block = content.IndexOf( '{' );
			if ( block >= 0 )
				content = content[..block];

			content = content.Trim();
			var space = content.IndexOfAny( [' ', '\t'] );
			if ( space < 0 )
				continue;

			raw.TryAdd( content[..space], content[space..].Trim() );
		}

		var groups = new Dictionary<string, List<(int Index, Word[] Words)>>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (name, words) in raw )
		{
			if ( name.StartsWith( "V_", StringComparison.OrdinalIgnoreCase ) )
				continue;

			var parsed = Words( words, raw, exists );
			if ( parsed.Length == 0 )
				continue;

			var digits = name.Length - name.AsSpan().TrimEnd( "0123456789" ).Length;
			var group = name[..^digits].ToUpperInvariant();
			var index = digits > 0 ? int.Parse( name[^digits..], CultureInfo.InvariantCulture ) : -1;
			if ( !groups.TryGetValue( group, out var list ) )
				groups[group] = list = [];
			list.Add( (index, parsed) );
		}

		return groups.ToDictionary( x => x.Key, x => x.Value.OrderBy( y => y.Index ).Select( y => y.Words ).ToList(), StringComparer.OrdinalIgnoreCase );
	}

	static Word[] Words( string sentence, Dictionary<string, string> raw, Func<string, bool> exists )
	{
		var folder = "vox/";
		var pitch = 100f;
		var volume = 100f;
		var result = new List<Word>();
		foreach ( var token in sentence.Split( [' ', '\t'], StringSplitOptions.RemoveEmptyEntries ) )
		{
			var word = token;
			var slash = word.LastIndexOf( '/' );
			if ( slash >= 0 )
			{
				folder = word[..(slash + 1)].ToLowerInvariant();
				word = word[(slash + 1)..];
				if ( word.StartsWith( '(' ) )
				{
					(pitch, volume) = Parameters( word, pitch, volume );
					continue;
				}
			}

			// The engine speaks punctuation as the folder's _comma and _period waves.
			var pause = word.EndsWith( ',' ) ? "_comma" : word.EndsWith( '.' ) ? "_period" : null;
			word = word.TrimEnd( ',', '.' );
			var (wordPitch, wordVolume) = (pitch, volume);
			var open = word.IndexOf( '(' );
			if ( open >= 0 )
			{
				(wordPitch, wordVolume) = Parameters( word[open..], pitch, volume );
				word = word[..open];
			}

			if ( word.Length > 0 && Spoken( word, folder, raw, exists ) is { Length: > 0 } waves )
				result.Add( new Word( waves, wordPitch / 100f, wordVolume / 100f, word.StartsWith( "V_MY", StringComparison.OrdinalIgnoreCase ) ) );

			if ( pause is not null && exists( $"sound/{folder}{pause}.wav" ) )
				result.Add( new Word( [$"sound/{folder}{pause}.wav"], 1, volume / 100f, false ) );
		}

		return [.. result];
	}

	/// <summary>
	/// The waves a word may be. A virtual word is one of its numbered alternatives (V_MYNAMES0, V_MYNAMES1). The engine computes
	/// distances, bearings and counts that have none; those read as a number from the speaker's V_NUMBERS set, others are dropped.
	/// </summary>
	static string[] Spoken( string word, string folder, Dictionary<string, string> raw, Func<string, bool> exists )
	{
		if ( !word.StartsWith( "V_", StringComparison.OrdinalIgnoreCase ) )
			return exists( $"sound/{folder}{word.ToLowerInvariant()}.wav" ) ? [$"sound/{folder}{word.ToLowerInvariant()}.wav"] : [];

		var alternatives = Alternatives( word, raw );
		if ( alternatives.Length == 0 && Regex.IsMatch( word, "NUM|NBR|DIST|DIR|GRID", RegexOptions.IgnoreCase ) )
			alternatives = Alternatives( $"V_NUMBERS{word[^1]}", raw );

		return alternatives.Select( x => $"sound/{folder}{x.ToLowerInvariant()}.wav" ).Where( exists ).ToArray();
	}

	static string[] Alternatives( string name, Dictionary<string, string> raw ) => raw
		.Where( x => x.Key.Length > name.Length && x.Key.StartsWith( name, StringComparison.OrdinalIgnoreCase ) && x.Key[name.Length..].All( char.IsAsciiDigit ) )
		.OrderBy( x => int.Parse( x.Key[name.Length..], CultureInfo.InvariantCulture ) )
		.Select( x => x.Value.Split( [' ', '\t', '('], StringSplitOptions.RemoveEmptyEntries ).FirstOrDefault()?.TrimEnd( ',', '.' ) )
		.Where( x => !string.IsNullOrEmpty( x ) && !x.Contains( '/' ) )
		.ToArray();

	static (float Pitch, float Volume) Parameters( string text, float pitch, float volume )
	{
		foreach ( Match match in Regex.Matches( text, "([pv])\\s*(\\d+)", RegexOptions.IgnoreCase ) )
		{
			var value = float.Parse( match.Groups[2].Value, CultureInfo.InvariantCulture );
			if ( char.ToLowerInvariant( match.Groups[1].Value[0] ) == 'p' )
				pitch = value;
			else
				volume = value;
		}

		return (pitch, volume);
	}
}
