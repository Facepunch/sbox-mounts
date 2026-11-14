namespace Source1;

/// <summary>Exports converted numeric defaults without exposing or executing config commands.</summary>
internal sealed class SourceSkillConfigLoader( SourceSkillConfig config ) : ResourceLoader<BaseMount>
{
	protected override object Load() => System.Text.Json.JsonSerializer.Serialize( config.Values );
}
