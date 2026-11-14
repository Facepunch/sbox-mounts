namespace Source1;

/// <summary>Exposes converted weapon info without requiring a model to be loaded first.</summary>
internal sealed class SourceWeaponScriptLoader( ModelWeaponData data ) : ResourceLoader<BaseMount>
{
	protected override object Load() => System.Text.Json.JsonSerializer.Serialize( data );
}
