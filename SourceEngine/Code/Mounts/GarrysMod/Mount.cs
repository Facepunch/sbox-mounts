namespace Source1.GarrysMod;

public sealed class Mount : SourceGameMount
{
	protected override string ContentDirectory => "garrysmod";
	protected override string WeaponScriptPrefix => "scripts/weapons/";
	public override string Ident => "gmod";
	public override string Title => "Garry's Mod";
	public override long AppId => GarrysModAppId;
}
