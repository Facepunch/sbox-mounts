using System.Text.Json;

namespace Source1;

/// <summary>Exports parsed sentences or response rules as JSON; the game defines the shapes it reads.</summary>
internal sealed class SourceSpeechLoader( object data ) : ResourceLoader<BaseMount>
{
	static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
	};

	protected override object Load() => JsonSerializer.Serialize( data, Options );
}
