namespace NS2;

[Title( "Harvester" ), Category( "Natural Selection 2" )]
public sealed class Harvester : AlienStructure
{
	protected override GrowthStats Stats => new( 38, 0.5f, 2000, 200, 150, 2300, 320 );

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer ) => Configure<Harvester>( root, renderer, "Harvester" );

	protected override void OnUpdate()
	{
		if ( Health <= 0 || GameObject.IsDestroyed ) return;
		base.OnUpdate();
		if ( graph is null || !Renderer.Animation.IsValid ) return;
		graph.Set( "built", IsBuilt ? 1 : 0 );
		graph.Set( "active", IsBuilt ? 1 : 0 );
		graph.Set( "alive", 1 );
		graph.Update( age, Renderer.Animation.Parameters );
	}
}
