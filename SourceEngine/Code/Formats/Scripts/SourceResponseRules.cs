using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Source1;

/// <summary>
/// Reads the talker response rules (scripts/talker/response_rules.txt and its includes) into concept -> rules. A rule keeps
/// its required criteria other than the concept, with enumerations resolved; weighted criteria only rank rules and are dropped.
/// A response is a wave, a sound script or a sentence group. Scenes become their line's wave: a choreographed scene
/// scenes/X.vcd speaks sound/vo/X.wav, kept with its $gender token when either gender's wave exists. Other scenes are dropped.
/// </summary>
internal static class SourceResponseRules
{
	internal sealed record Response( string Wave, string Sound, string Sentence, float Weight );
	internal sealed record Rule( Dictionary<string, string> Criteria, Response[] Responses );

	sealed record Criterion( string Key, string Value, bool Required );
	sealed record Entry( string Type, string Value, float Weight );
	readonly record struct Token( string Text, int Line );

	sealed class State
	{
		public readonly Dictionary<string, string> Enumerations = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, Criterion> Criteria = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, List<Entry>> Responses = new( StringComparer.OrdinalIgnoreCase );
		public readonly List<(List<Criterion> Criteria, List<string> Responses)> Rules = [];
		public readonly HashSet<string> Files = new( StringComparer.OrdinalIgnoreCase );
	}

	/// <summary>Parses <paramref name="path"/> and its includes; <paramref name="read"/> returns a script's text or null when it is missing.</summary>
	internal static Dictionary<string, List<Rule>> Parse( string path, Func<string, string> read, Func<string, bool> exists )
	{
		var state = new State();
		Read( state, path, read );

		var concepts = new Dictionary<string, List<Rule>>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (criteria, responses) in state.Rules )
		{
			var concept = criteria.FirstOrDefault( x => x.Key.Equals( "concept", StringComparison.OrdinalIgnoreCase ) )?.Value;
			if ( string.IsNullOrEmpty( concept ) )
				continue;

			var lines = responses.SelectMany( x => Flatten( state, x, 1, exists, 0 ) ).ToArray();
			if ( lines.Length == 0 )
				continue;

			var required = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
			foreach ( var criterion in criteria.Where( x => x.Required && !x.Key.Equals( "concept", StringComparison.OrdinalIgnoreCase ) ) )
				required[criterion.Key.ToLowerInvariant()] = Regex.Replace( criterion.Value, "\\[([^\\]]+)\\]", m => state.Enumerations.GetValueOrDefault( m.Groups[1].Value, m.Value ) ).ToLowerInvariant();

			var key = concept.ToLowerInvariant();
			if ( !concepts.TryGetValue( key, out var rules ) )
				concepts[key] = rules = [];
			rules.Add( new Rule( required, lines ) );
		}

		return concepts;
	}

	static IEnumerable<Response> Flatten( State state, string group, float weight, Func<string, bool> exists, int depth )
	{
		if ( depth > 8 || !state.Responses.TryGetValue( group, out var entries ) || entries.Count == 0 )
			yield break;

		var total = entries.Sum( x => x.Weight );
		foreach ( var entry in entries )
		{
			var share = total > 0 ? weight * entry.Weight / total : 0;
			switch ( entry.Type.ToLowerInvariant() )
			{
				case "response":
					foreach ( var nested in Flatten( state, entry.Value, share, exists, depth + 1 ) )
						yield return nested;
					break;
				case "scene":
					if ( SceneWave( entry.Value, exists ) is { } wave )
						yield return new Response( wave, null, null, share );
					break;
				case "speak":
					yield return new Response( null, entry.Value, null, share );
					break;
				case "sentence":
					yield return new Response( null, null, entry.Value.TrimStart( '!' ), share );
					break;
			}
		}
	}

	static string SceneWave( string scene, Func<string, bool> exists )
	{
		scene = scene.Replace( '\\', '/' ).ToLowerInvariant();
		if ( !scene.StartsWith( "scenes/", StringComparison.Ordinal ) || !scene.EndsWith( ".vcd", StringComparison.Ordinal ) )
			return null;

		var wave = $"sound/vo/{scene["scenes/".Length..^".vcd".Length]}.wav";
		var genders = wave.Contains( "$gender" ) ? new[] { wave.Replace( "$gender", "male" ), wave.Replace( "$gender", "female" ) } : [wave];
		return genders.Any( exists ) ? wave : null;
	}

	static void Read( State state, string path, Func<string, string> read )
	{
		if ( !state.Files.Add( path ) || read( path ) is not { } text )
			return;

		var tokens = Tokenize( text );
		var i = 0;
		while ( i < tokens.Count - 1 )
		{
			var keyword = tokens[i++];
			switch ( keyword.Text.ToLowerInvariant() )
			{
				case "#include":
					Read( state, $"scripts/{tokens[i++].Text.Replace( '\\', '/' ).ToLowerInvariant()}", read );
					break;
				case "enumeration":
					var enumeration = tokens[i++].Text;
					foreach ( var line in Block( tokens, ref i ) )
					{
						if ( line.Count >= 2 )
							state.Enumerations[$"{enumeration}::{line[0]}"] = line[1];
					}
					break;
				case "criterion":
					var criterion = Rest( tokens, ref i, keyword.Line );
					if ( criterion.Count >= 3 )
						state.Criteria[criterion[0]] = Inline( criterion.Skip( 1 ).ToList() );
					break;
				case "response":
					var name = tokens[i++].Text;
					var entries = new List<Entry>();
					if ( i < tokens.Count && tokens[i].Text == "{" )
					{
						foreach ( var line in Block( tokens, ref i ) )
						{
							if ( ResponseEntry( line ) is { } entry )
								entries.Add( entry );
						}
					}
					else if ( ResponseEntry( Rest( tokens, ref i, keyword.Line ) ) is { } entry )
					{
						entries.Add( entry );
					}
					state.Responses.TryAdd( name, entries );
					break;
				case "rule":
					i++;
					var criteria = new List<Criterion>();
					var responses = new List<string>();
					foreach ( var line in Block( tokens, ref i ) )
					{
						switch ( line[0].ToLowerInvariant() )
						{
							case "criteria":
							case "criterion":
								criteria.AddRange( line.Skip( 1 ).Select( x => state.Criteria.GetValueOrDefault( x ) ).Where( x => x is not null ) );
								break;
							case "response":
								responses.AddRange( line.Skip( 1 ) );
								break;
							case "matchonce":
							case "applycontext":
								break;
							default:
								if ( line.Count >= 2 )
									criteria.Add( Inline( line ) );
								break;
						}
					}
					state.Rules.Add( (criteria, responses) );
					break;
			}
		}
	}

	static Entry ResponseEntry( List<string> line )
	{
		if ( line.Count < 2 )
			return null;

		var weight = 1f;
		for ( var j = 2; j < line.Count - 1; j++ )
		{
			if ( line[j].Equals( "weight", StringComparison.OrdinalIgnoreCase ) && float.TryParse( line[j + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) )
				weight = value;
		}

		return new Entry( line[0], line[1], weight );
	}

	static Criterion Inline( List<string> line ) => new( line[0], line[1], line.Skip( 2 ).Any( x => x.Equals( "required", StringComparison.OrdinalIgnoreCase ) ) );

	/// <summary>The remaining tokens on a line, as the engine reads optional parameters.</summary>
	static List<string> Rest( List<Token> tokens, ref int i, int line )
	{
		var result = new List<string>();
		while ( i < tokens.Count && tokens[i].Line == line && tokens[i].Text is not "{" and not "}" )
			result.Add( tokens[i++].Text );
		return result;
	}

	/// <summary>The lines of a braced block, starting at its opening brace.</summary>
	static List<List<string>> Block( List<Token> tokens, ref int i )
	{
		var lines = new List<List<string>>();
		if ( i >= tokens.Count || tokens[i].Text != "{" )
			return lines;

		i++;
		while ( i < tokens.Count && tokens[i].Text != "}" )
		{
			var line = Rest( tokens, ref i, tokens[i].Line );
			if ( line.Count > 0 )
				lines.Add( line );
			else
				i++;
		}
		i++;
		return lines;
	}

	static List<Token> Tokenize( string text )
	{
		var tokens = new List<Token>();
		var line = 0;
		var i = 0;
		while ( i < text.Length )
		{
			var c = text[i];
			if ( c == '\n' )
			{
				line++;
				i++;
			}
			else if ( char.IsWhiteSpace( c ) )
			{
				i++;
			}
			else if ( c == '/' && i + 1 < text.Length && text[i + 1] == '/' )
			{
				while ( i < text.Length && text[i] != '\n' )
					i++;
			}
			else if ( c is '{' or '}' )
			{
				tokens.Add( new Token( c.ToString(), line ) );
				i++;
			}
			else if ( c == '"' )
			{
				var end = text.IndexOf( '"', i + 1 );
				if ( end < 0 )
					end = text.Length;
				tokens.Add( new Token( text[(i + 1)..end], line ) );
				i = end + 1;
			}
			else
			{
				var start = i;
				while ( i < text.Length && !char.IsWhiteSpace( text[i] ) && text[i] is not '{' and not '}' and not '"' )
					i++;
				tokens.Add( new Token( text[start..i], line ) );
			}
		}
		return tokens;
	}
}
