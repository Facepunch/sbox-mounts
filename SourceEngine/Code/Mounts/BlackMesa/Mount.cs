namespace Source1.BlackMesa;

public sealed class Mount : SourceGameMount
{
	protected override string ContentDirectory => "bms";
	internal override IEnumerable<string> PlayerStartClasses => ["info_player_marine", "info_player_scientist"];
	public override string Ident => "bms";
	public override string Title => "Black Mesa";
	public override long AppId => 362890;
	internal override BspFormat CreateMapFormat() => new BlackMesa.MapFormat();
	internal override string ResolveShader( string shader ) => shader == "bms_lightmapped_4wayblend" ? "lightmapped_4wayblend" : shader;
}
