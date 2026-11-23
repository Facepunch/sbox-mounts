using Sandbox.Npcs.Layers;
using System.Threading.Tasks;

namespace Overgrowth;

internal sealed class CharacterPrefab( string title, string modelName ) : ResourceLoader<GameMount>
{
	PrefabFile prefab;

	protected override async Task<object> LoadAsync()
	{
		var modelPath = Host.MountedPath( modelName ) + ".vmdl";
		var loader = (CharacterLoader)Host.GetByPath( modelPath );
		var model = (Model)await loader.GetOrCreate();
		if ( model is null || model.IsError )
			throw new System.IO.InvalidDataException( $"Could not build {title} prefab with missing model {modelPath}" );
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", title )
			.WithMetadata( "Group", Host.Title ).WithMetadata( "PreviewModel", modelPath );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var go = new GameObject( true, title );
			Configure( go, model, title, loader );
		}
		return prefab = builder.Create();
	}

	static void Configure( GameObject go, Model model, string title, CharacterLoader character )
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
		var hitboxes = visuals.AddComponent<ModelHitboxes>();
		hitboxes.Hulls = character.HitVolumes;
		hitboxes.Target = go;
		hitboxes.Renderer = renderer;

		var hull = new GameObject( go, true, "collider" );
		hull.Tags.Add( "playercontroller" );
		var collider = hull.AddComponent<CapsuleCollider>();
		collider.Radius = radius;
		collider.Start = Vector3.Up * radius;
		collider.End = Vector3.Up * (height - radius);

		var body = go.AddComponent<Rigidbody>();
		body.MotionEnabled = false;
		body.EnableImpactDamage = false;
		body.CollisionEventsEnabled = true;
		body.Locking = new PhysicsLock { Pitch = true, Roll = true };
		var agent = go.AddComponent<NavMeshAgent>();
		agent.Radius = radius;
		agent.Height = height;
		// BrainSpeciesUpdate reserves off-mesh jump routes for rabbits.
		agent.AllowAllLinkTypes = character.Species == "rabbit";
		go.AddComponent<NavigationLayer>().RunSpeed = 8f * 40f;
		var animation = go.AddComponent<AnimationLayer>();
		animation.EnableFootstepSounds = false;
		var npc = go.AddComponent<RabbitGuard>();
		go.AddComponent<RabbitAirContact>().Fighter = npc;
		npc.MovementCapsule = collider;
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
