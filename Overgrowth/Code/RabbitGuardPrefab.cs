using Sandbox.Npcs.Layers;

namespace Overgrowth;

internal sealed record RabbitActor( string Title, string Path )
{
	internal string ModelName => $"entities/npcs/{Title.ToLowerInvariant()}";
	internal string PrefabName => ModelName + ".prefab";
	internal static readonly RabbitActor[] All =
	[
		new( "Turner", "Data/Objects/IGF_Characters/IGF_TurnerActor.xml" ),
		new( "Rabbot", "Data/Objects/characters/rabbot_actor.xml" )
	];
}

internal sealed class RabbitGuardPrefab( RabbitActor actor ) : ResourceLoader<GameMount>
{
	PrefabFile prefab;

	protected override object Load()
	{
		var modelPath = Host.MountedPath( actor.ModelName ) + ".vmdl";
		var model = Model.Load( modelPath );
		if ( model is null || model.IsError )
			throw new System.IO.InvalidDataException( $"Could not build {actor.Title} prefab with missing model {modelPath}" );
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", actor.Title )
			.WithMetadata( "Group", Host.Title ).WithMetadata( "PreviewModel", modelPath );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var go = new GameObject( true, actor.Title );
			Configure( go, model, actor.Title );
		}
		return prefab = builder.Create();
	}

	static void Configure( GameObject go, Model model, string title )
	{
		go.Tags.Add( "npc" );
		go.NetworkMode = NetworkMode.Object;
		float height = model.Bounds.Size.z;
		const float radius = 0.45f * 40f;

		var visuals = new GameObject( go, true, "model" );
		// aschar.as places animated frames at the bottom of the leg sphere (the NPC root).
		var renderer = visuals.AddComponent<SkinnedModelRenderer>();
		renderer.Model = model;
		renderer.UseAnimGraph = true;

		var hull = new GameObject( go, true, "collider" );
		hull.Tags.Add( "playercontroller" );
		var collider = hull.AddComponent<CapsuleCollider>();
		collider.Radius = radius;
		collider.Start = Vector3.Up * radius;
		collider.End = Vector3.Up * (height - radius);

		var body = go.AddComponent<Rigidbody>();
		body.MotionEnabled = false;
		body.EnableImpactDamage = false;
		body.Locking = new PhysicsLock { Pitch = true, Roll = true };
		var agent = go.AddComponent<NavMeshAgent>();
		agent.Radius = radius;
		agent.Height = height;
		go.AddComponent<NavigationLayer>().RunSpeed = 8f * 40f;
		var animation = go.AddComponent<AnimationLayer>();
		animation.EnableFootstepSounds = false;
		var npc = go.AddComponent<RabbitGuard>();
		npc.DisplayName = title;
		npc.Renderer = renderer;
		npc.Animation = animation;
		npc.EyeHeight = -model.Bounds.Mins.z + model.Bounds.Maxs.z * 0.8f;
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		prefab = null;
	}
}
