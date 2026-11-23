using System;

namespace NS2;

internal sealed class AlienPrefab( string title, string directory, Action<GameObject, SkinnedModelRenderer> configure ) : ResourceLoader<GameMount>
{
	string ModelPath => $"mount://ns2/{directory}.model.vmdl";
	PrefabFile prefab;

	internal static void Register( MountContext context, string title, Action<GameObject, SkinnedModelRenderer> configure )
	{
		var name = title.ToLowerInvariant();
		var directory = $"ns2/models/alien/{name}/{name}";
		var graph = $"{directory}.animation_graph";
		context.Add( ResourceType.AnimationGraph, graph, new AnimationGraphLoader( $"{directory}.model", graph, npcPlayback: true ) );
		var loader = new AlienPrefab( title, directory, configure );
		loader.Tags.Add( "npc" );
		context.Add( ResourceType.PrefabFile, $"entities/npcs/{name}.prefab", loader );
	}

	protected override object Load()
	{
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", title )
			.WithMetadata( "Group", Host.Title ).WithMetadata( "PreviewModel", ModelPath )
			.WithMetadata( "PreviewSequence", "idle" );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, title ) { NetworkMode = NetworkMode.Object };
			root.Tags.Add( "npc", "removable" );
			var renderer = new GameObject( root, true, "Model" ).AddComponent<SkinnedModelRenderer>();
			renderer.Model = Model.Load( ModelPath );
			renderer.UseAnimGraph = true;
			renderer.Animation.Graph = Sandbox.Animation.Graph.Load( $"mount://ns2/{directory}.animation_graph.ag" );
			root.AddComponent<ModelHitboxes>().Renderer = renderer;
			configure( root, renderer );
		}
		return prefab = builder.Create();
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		base.Shutdown();
	}
}
