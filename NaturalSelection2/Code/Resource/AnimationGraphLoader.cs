namespace NS2;

/// <summary>A Spark graph references sequences on a model. The mount owns and caches the compiled AG2 resource.</summary>
internal sealed class AnimationGraphLoader( string modelFile, string graphFile, bool npcPlayback = false ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var model = Host.GetByPath( $"mount://{Host.Ident}/{modelFile}.vmdl" ) as ModelLoader;
		return model?.BuildGraph( SparkAnimationGraph.Read( Host.ReadAllBytes( graphFile ) ), Path, npcPlayback );
	}
}
