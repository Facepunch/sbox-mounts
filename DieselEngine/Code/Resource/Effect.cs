namespace Diesel;

[AssetType( Name = "PAYDAY Effect", Extension = "pdeffect", Category = "Effects" )]
public sealed class PaydayEffectResource : GameResource
{
	public string SourcePath { get; set; }
	public string SourceXml { get; set; }
	public string[] TexturePaths { get; set; } = [];
	public string[] UnsupportedOperators { get; set; } = [];
	public string[] ChildEffectPaths { get; set; } = [];
	internal EffectDefinition Definition { get; set; }
}

sealed class PaydayEffectLoader( string source ) : ResourceLoader<PaydayMount>
{
	protected override object Load()
	{
		var definition = Host.GetEffect( source );
		return new PaydayEffectResource
		{
			SourcePath = source, SourceXml = definition.Source, Definition = definition,
			TexturePaths = definition.Textures.Select( t => $"mount://payday/{t}.texture.vtex" ).ToArray(),
			UnsupportedOperators = definition.Unsupported,
			ChildEffectPaths = definition.ChildEffects.Select( p => $"mount://payday/{p}.pdeffect" ).ToArray()
		};
	}
}

sealed class PaydayEffectPrefabLoader( string source, bool debris = false ) : ResourceLoader<PaydayMount>
{
	PrefabFile prefab;
	protected override object Load()
	{
		var builder = new PrefabBuilder().WithName( Path )
			.WithMetadata( "Title", source.Split( '/' ).Last().Replace( '_', ' ' ) )
			.WithMetadata( "Group", debris ? "PAYDAY Debris" : "PAYDAY Effects" );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( false, source.Split( '/' ).Last() );
			if ( debris ) root.AddComponent<PaydayDebrisEmitter>().SourceUnit = source;
			else root.AddComponent<PaydayEffect>().EffectPath = source;
			root.Enabled = true;
		}
		return prefab = builder.Create();
	}
	protected override void Shutdown() { PrefabBuilder.Destroy( prefab ); prefab = null; base.Shutdown(); }
}

sealed class PaydayDebrisModelLoader( string unit, string body ) : ResourceLoader<PaydayMount>
{
	protected override object Load() => Host.GetDebris( unit ).First( d => d.Body.Name == body ).Geometry.Model;
}
