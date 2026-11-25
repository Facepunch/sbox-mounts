namespace NS2;

[Title( "Harvester" ), Category( "Natural Selection 2" )]
public sealed class Harvester : AlienStructure
{
	internal const string PainSound = "alien/structures/harvester_wound";
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.BodyPose pose { get; set; }
	protected override GrowthStats Stats => new( 38, 0.5f, 2000, 200, 150, 2300, 320 );

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer ) => Configure<Harvester>( root, renderer, "Harvester" );

	protected override void OnUpdate()
	{
		if ( Health <= 0 || GameObject.IsDestroyed ) return;
		base.OnUpdate();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		float intensity = FlinchIntensity;
		output?.Set( "intensity", intensity );
		if ( !Networking.IsHost )
		{
			pose.Write( output, age );
			return;
		}
		if ( graph is null ) return;
		graph.Set( "built", IsBuilt ? 1 : 0 );
		graph.Set( "active", IsBuilt ? 1 : 0 );
		graph.Set( "alive", 1 );
		graph.Set( "flinch", intensity > 0 ? 1 : 0 );
		graph.Set( "onfire", GameObject.Tags.Has( "burning" ) ? 1 : 0 );
		graph.Update( age, output );
		pose = graph.GetBodyPose();
	}

	protected override GameObject CreateRagdoll()
	{
		if ( graph is null ) return NpcCorpse.Attach( base.CreateRagdoll(), 3 );
		graph.Set( "built", IsBuilt ? 1 : 0 );
		graph.Set( "onfire", 0 );
		return NpcCorpse.AnimateUntilRagdoll( Renderer, graph, age, flinchEnd );
	}
}
