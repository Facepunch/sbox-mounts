namespace Source1;

public sealed class SourceGarrysModMount : SourceGameMount
{
	protected override string ContentDirectory => "garrysmod";
	public override string Ident => "gmod";
	public override string Title => "Garry's Mod";
	public override long AppId => GarrysModAppId;
}
