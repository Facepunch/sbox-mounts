using System.Text.Json;
using System;

namespace Source1;

/// <summary>One word of a sentence: the waves it may be, and whether a speaker always says the same one (its name, its number).</summary>
public sealed class SourceSentenceWord
{
	public string[] Waves { get; set; } = [];
	public float Pitch { get; set; } = 1;
	public float Volume { get; set; } = 1;
	public bool Speaker { get; set; }
}

/// <summary>A response rule's line: a wave (with a $gender token for gendered voices), a sound script or a sentence group.</summary>
public sealed class SourceResponse
{
	public string Wave { get; set; }
	public string Sound { get; set; }
	public string Sentence { get; set; }
	public float Weight { get; set; }
}

/// <summary>A response rule: the criteria a speaker must meet and the lines it picks from.</summary>
public sealed class SourceResponseRule
{
	public Dictionary<string, string> Criteria { get; set; } = [];
	public SourceResponse[] Responses { get; set; } = [];
}

/// <summary>A bot chatter wave and what it is said of: an enemy count (1, 2, 3 or Many) and a place (ANY, UNDEFINED or a place name).</summary>
public sealed class SourceChatterLine
{
	public string Wave { get; set; }
	public string Count { get; set; }
	public string Place { get; set; }
}

/// <summary>A Counter-Strike bot chatter phrase: the waves a bot says one of.</summary>
public sealed class SourceChatter
{
	public SourceChatterLine[] Lines { get; set; } = [];
}

/// <summary>Reads the engine mount's converted sentences.txt, talker response rules and bot chatter, once per mount.</summary>
static class SourceSpeech
{
	static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
	static readonly Dictionary<string, Dictionary<string, List<SourceSentenceWord[]>>> SentenceGroups = new();
	static readonly Dictionary<string, Dictionary<string, List<SourceResponseRule>>> ResponseRules = new();
	static readonly Dictionary<string, Dictionary<string, SourceChatter>> BotChatter = new();

	/// <summary>Sentence groups by name, such as COMBINE_ALERT; each is a list of alternatives.</summary>
	public static Dictionary<string, List<SourceSentenceWord[]>> Sentences( string mountName ) => Read( SentenceGroups, mountName, "scripts/sentences.txt.json" );

	/// <summary>Response rules by lower case concept, such as tlk_hello.</summary>
	public static Dictionary<string, List<SourceResponseRule>> Rules( string mountName ) => Read( ResponseRules, mountName, "scripts/talker/response_rules.txt.json" );

	/// <summary>Bot chatter phrases (BotChatter.db) by name, such as EnemySpotted.</summary>
	public static Dictionary<string, SourceChatter> Chatter( string mountName ) => Read( BotChatter, mountName, "botchatter.db.json" );

	static Dictionary<string, T> Read<T>( Dictionary<string, Dictionary<string, T>> cache, string mountName, string path )
	{
		if ( cache.TryGetValue( mountName ?? "", out var data ) )
			return data;

		var mount = Sandbox.Mounting.Directory.Get( mountName ?? "" );
		if ( mount?.IsMounted != true )
			return [];

		// The mount's text loaders finish synchronously; a mount without the script (Counter-Strike) has none.
		var load = mount.GetByPath( $"mount://{mountName}/{path}" )?.GetOrCreate();
		if ( load is { IsCompleted: false } )
			return [];

		var json = load is { IsCompletedSuccessfully: true } ? load.Result as string : null;
		data = json is null ? [] : JsonSerializer.Deserialize<Dictionary<string, T>>( json, Options ) ?? [];
		cache[mountName] = new Dictionary<string, T>( data, StringComparer.OrdinalIgnoreCase );
		return cache[mountName];
	}
}
