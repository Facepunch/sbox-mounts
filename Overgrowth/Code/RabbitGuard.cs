using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace Overgrowth;

[Library( "overgrowth_turner" )]
public sealed partial class RabbitGuard : BaseNpc
{
	internal const string ModelName = "entities/npcs/turner";
	internal const string ActorPath = "Data/Objects/IGF_Characters/IGF_TurnerActor.xml";
	internal const string Idle = "r_idle", Watch = "r_dialogue_shade", Threat = "r_combatidlethreat", Locomotion = "movement";
	internal const string Kick = "r_frontkick", Knee = "r_kneestrike", Jab = "r_jab", Roundhouse = "r_roundhouse";

	[Sync] public int AlertStage { get; private set; }
	TimeUntil nextVisionCheck = 1f;
	float enemySeen;
	string playing;
	[Sync] string Stance { get; set; } = Idle;
	RabbitGuardAnimations boundAnimations;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Health < 1 || !Renderer.IsValid() || Renderer.Model is null ) return;
		if ( boundAnimations is null || Renderer.Animation.Graph is null )
		{
			var mount = Sandbox.Mounting.Directory.Get( "overgrowth" );
			if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is not CharacterLoader loader || loader.GetGuardAnimations() is not { } ready ) return;
			boundAnimations = ready;
			Renderer.Animation.Graph = ready.Graph;
			playing = null;
		}
		if ( Stance == Locomotion ) UpdateGait();
		if ( playing == Stance ) return;
		playing = Stance;
		Renderer.Animation.Parameters.Set( "stance", playing );
	}

	protected override void Think()
	{
		if ( !Renderer.IsValid() || boundAnimations is null ) return;
		if ( nextVisionCheck <= 0 )
		{
			nextVisionCheck = Random.Shared.Float( 0.2f, 0.3f );
			UpdateAwareness();
		}
		UpdateCombat();
	}

	GameObject enemy;
	Vector3 lastSeen;
	bool visible;
	GameObject watching;

	void UpdateAwareness()
	{
		IEnumerable<GameObject> candidates = Scene.GetAllComponents<PlayerController>().Select( x => x.GameObject )
			.Concat( Scene.GetAllComponents<BaseNpc>().Where( x => x is not RabbitGuard && x.Health >= 1 ).Select( x => x.GameObject ) );
		GameObject closest = null;
		float nearest = float.MaxValue;
		foreach ( var candidate in candidates )
		{
			var delta = CalculateEyePosition( candidate ) - EyePosition;
			float distance = delta.Length;
			if ( distance >= nearest ) continue;
			var angles = (WorldRotation.Inverse * delta).EulerAngles.Normal;
			bool focus = distance <= 4000 && MathF.Abs( angles.yaw ) <= 36 && MathF.Abs( angles.pitch ) <= 36;
			bool peripheral = distance <= 160 && MathF.Abs( angles.yaw ) <= 81 && MathF.Abs( angles.pitch ) <= 81;
			if ( !focus && !peripheral ) continue;
			if ( !HasSight( candidate ) ) continue;
			closest = candidate;
			nearest = distance;
		}
		visible = closest.IsValid();
		if ( visible )
		{
			if ( closest != watching ) enemySeen = 0;
			watching = closest;
			enemySeen = Math.Min( 1, enemySeen + 0.5f - 0.4f * nearest / 4000f );
			if ( enemySeen >= 1 ) enemy = closest;
			if ( closest == enemy ) lastSeen = closest.WorldPosition;
		}
		else enemySeen = Math.Max( 0, enemySeen - 0.05f );
		visible = visible && closest == enemy;
		AlertStage = enemy.IsValid() ? 2 : enemySeen > 0 ? 1 : 0;
	}

	bool HasSight( GameObject other ) => !Scene.Trace.Ray( EyePosition, CalculateEyePosition( other ) )
		.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( other ).WithoutTags( "trigger" ).Run().Hit;

}

internal sealed class RabbitGuardPrefab : ResourceLoader<GameMount>
{
	PrefabFile prefab;

	protected override object Load()
	{
		var modelPath = Host.MountedPath( RabbitGuard.ModelName ) + ".vmdl";
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", "Turner" )
			.WithMetadata( "Group", Host.Title ).WithMetadata( "PreviewModel", modelPath );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var go = new GameObject( true, "Turner" );
			Configure( go, Model.Load( modelPath ) );
		}
		return prefab = builder.Create();
	}

	internal static void Configure( GameObject go, Model model )
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
		npc.DisplayName = "Turner";
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
