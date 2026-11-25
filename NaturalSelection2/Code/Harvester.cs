namespace NS2;

[Title( "Harvester" ), Category( "Natural Selection 2" )]
public sealed class Harvester : AlienStructure
{
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.BodyPose pose { get; set; }
	protected override GrowthStats Stats => new( 38, 0.5f, 2000, 200, 150, 2300, 320 );

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer ) => Configure<Harvester>( root, renderer, "Harvester" );

	protected override void OnUpdate()
	{
		if ( Health <= 0 || GameObject.IsDestroyed ) return;
		base.OnUpdate();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		if ( !Networking.IsHost )
		{
			pose.Write( output, age );
			return;
		}
		if ( graph is null ) return;
		graph.Set( "built", IsBuilt ? 1 : 0 );
		graph.Set( "active", IsBuilt ? 1 : 0 );
		graph.Set( "alive", 1 );
		graph.Update( age, output );
		pose = graph.GetBodyPose();
	}
}
