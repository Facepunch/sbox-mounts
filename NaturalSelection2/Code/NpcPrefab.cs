using System;

namespace NS2;

internal sealed class NpcPrefab( string title, string directory, string tag, Action<GameObject, SkinnedModelRenderer> configure ) : ResourceLoader<GameMount>
{
	string ModelPath => $"mount://ns2/{directory}.model.vmdl";
	PrefabFile prefab;

	internal static string Register( MountContext context, string title, Action<GameObject, SkinnedModelRenderer> configure, string tag = "ns2_lifeform", string team = "alien", string modelName = null )
	{
		var name = title.ToLowerInvariant();
		modelName ??= name;
		var directory = $"ns2/models/{team}/{modelName}/{modelName}";
		var graph = $"{directory}.animation_graph";
		context.Add( ResourceType.AnimationGraph, graph, new AnimationGraphLoader( $"{directory}.model", graph, npcPlayback: true ) );
		var loader = new NpcPrefab( title, directory, tag, configure );
		loader.Tags.Add( "npc" );
		context.Add( ResourceType.PrefabFile, $"entities/npcs/{name}.prefab", loader );
		return $"{directory}.model";
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
			root.Tags.Add( "npc", "removable", tag );
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
