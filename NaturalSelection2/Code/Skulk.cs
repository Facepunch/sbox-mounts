using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace NS2;

internal sealed class SkulkPrefab : ResourceLoader<GameMount>
{
	const string ModelFile = "ns2/models/alien/skulk/skulk.model";
	const string ModelPath = $"mount://ns2/{ModelFile}.vmdl";
	const string WorldGraph = "ns2/models/alien/skulk/skulk.animation_graph";
	PrefabFile prefab;

	internal static void Register( MountContext context )
	{
		context.Add( ResourceType.AnimationGraph, WorldGraph, new AnimationGraphLoader( ModelFile, WorldGraph, npcPlayback: true ) );
		Skulk.RegisterSounds( context );
		var loader = new SkulkPrefab();
		loader.Tags.Add( "npc" );
		context.Add( ResourceType.PrefabFile, "entities/npcs/skulk.prefab", loader );
	}

	protected override object Load()
	{
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", "Skulk" )
			.WithMetadata( "Group", Host.Title ).WithMetadata( "PreviewModel", ModelPath )
			.WithMetadata( "PreviewSequence", "idle" );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, "Skulk" ) { NetworkMode = NetworkMode.Object };
			root.Tags.Add( "npc", "removable" );
			var renderer = new GameObject( root, true, "Model" ).AddComponent<SkinnedModelRenderer>();
			renderer.Model = Model.Load( ModelPath );
			renderer.UseAnimGraph = true;
			renderer.Animation.Graph = Sandbox.Animation.Graph.Load( $"mount://ns2/{WorldGraph}.ag" );
			root.AddComponent<ModelHitboxes>().Renderer = renderer;
			var hull = new GameObject( root, true, "Collider" );
			hull.Tags.Add( "playercontroller" );
			var sphere = hull.AddComponent<SphereCollider>();
			sphere.Center = Vector3.Up * SkulkMovement.Radius;
			sphere.Radius = SkulkMovement.Radius;
			var body = root.AddComponent<Rigidbody>();
			body.MassOverride = 45;
			body.EnableImpactDamage = false;
			body.Locking = new PhysicsLock { Pitch = true, Roll = true, Yaw = true };
			root.AddComponent<SkulkMovement>().RunSpeed = 7.25f * MapLoader.Scale;
			var senses = root.AddComponent<SensesLayer>();
			senses.SightRange = 20 * MapLoader.Scale;
			senses.FieldOfView = 360;
			var animation = root.AddComponent<AnimationLayer>();
			animation.EnableFootstepSounds = false;
			animation.TurnsBody = false;
			var npc = root.AddComponent<Skulk>();
			npc.Faction = Factions.Monster;
			npc.EyeHeight = 0.55f * MapLoader.Scale;
			npc.Health = 75;
			npc.DisplayName = "Skulk";
			npc.Renderer = renderer;
		}
		return prefab = builder.Create();
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		base.Shutdown();
	}
}

[Title( "Skulk" ), Category( "Natural Selection 2" )]
public sealed partial class Skulk : BaseNpc
{
	const float BiteEnergy = 5.85f;
	const float LeapEnergy = 45;
	const float Reach = 1.42f * MapLoader.Scale;
	[Property] public bool LeapUnlocked { get; set; } = true;
	bool Biting { get; set; }
	bool Leaping { get; set; }
	bool secondaryHeld;
	SkulkMovement mover;
	TimeUntil leapEnds;
	float energy = 100;
	SparkAnimationGraph.Playback world, weapon;
	Action<string> weaponTag;

	protected override void OnStart()
	{
		base.OnStart();
		mover = GetComponent<SkulkMovement>();
		if ( !Networking.IsHost ) return;
		ViewRotation = BodyRotation = Renderer.WorldRotation;
		standingYaw = runningYaw = BodyRotation.Angles().yaw;
		AnimationTime = 0;
		sneaky = Random.Shared.Next( 2 ) == 0;
		worldTag = OnWorldTag;
		PlayVoice( "spawn" );
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			world = new( model.PropGraph, model );
		if ( mount?.GetByPath( "mount://ns2/ns2/models/alien/skulk/skulk_view.model.vmdl" ) is ModelLoader viewModel )
		{
			weapon = new( viewModel.PropGraph, viewModel );
			weaponTag = OnWeaponTag;
			weapon.Set( "ability", "bite" );
			weapon.Set( "attack_speed", 1 );
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Networking.IsHost && Health > 0 )
		{
			if ( xenociding && detonation <= 0 )
			{
				Detonate();
				return;
			}
			Regenerate();
			UpdateFlinch();
			energy = Math.Min( 100, energy + Time.Delta * 10 );
			if ( Leaping && leapEnds <= 0 ) Leaping = false;
		}

		UpdateDashSound();
		UpdateAnimation();
	}

	protected override void Think()
	{
		bool attackLeap = UpdateCombat();
		bool travelLeap = UpdateTravel();
		bool press = attackLeap || travelLeap;
		if ( press && !secondaryHeld ) Leap();
		secondaryHeld = press;
	}

	bool UpdateCombat()
	{
		FiringParasite = false;
		var target = FindEnemy();
		var aimPoint = target.IsValid() ? CalculateAimPoint( target ) : default;
		if ( aimPenalty <= 0 ) aimScale = 1;
		bool aimReady = target.IsValid() && TrackAim( target, aimPoint );
		AimTurnRate = aimScale * (underFire > 0 || dealtDamage > 0 ? 5.5f : 5);
		Animation.SetLookTarget( target );
		if ( !target.IsValid() )
		{
			Investigate();
			Biting = false;
			return false;
		}

		bool mobile = target.GetComponent<PlayerController>().IsValid() || target.GetComponent<BaseNpc>().IsValid();
		var contact = target.GetComponent<Rigidbody>() is { PhysicsBody.IsValid: true } body
			? body.FindClosestPoint( EyePosition )
			: target.GetComponentInChildren<Collider>()?.FindClosestPoint( EyePosition ) ?? target.WorldPosition;
		var distance = EyePosition.Distance( contact );
		if ( distance < 1.4f * MapLoader.Scale )
		{
			if ( mobile ) ApproachEnemy( target, aimPoint, distance );
			else Movement.Stop();
			Biting = !(mobile && TryXenocide()) && energy >= BiteEnergy && (!mobile || aimReady);
			return false;
		}

		Biting = false;
		var aim = (aimPoint - EyePosition).Normal;
		bool leap = mobile && LeapUnlocked && energy > LeapEnergy + 10 && distance is > (5 * MapLoader.Scale) and < (18 * MapLoader.Scale)
			&& Vector3.Dot( ViewRotation.Forward, aim ) > 0.95f;
		if ( !leap && !xenociding ) TryParasite();
		ApproachEnemy( target, aimPoint, distance );
		return leap;
	}

	void Leap()
	{
		var velocity = GetComponent<Rigidbody>().Velocity * 0.5f;
		var forward = ViewRotation.Forward;
		velocity += forward.WithZ( 0 ).Normal * (7.6f * MapLoader.Scale);
		velocity.z = (forward.z + 0.5f) * (10.8f * MapLoader.Scale) + Math.Min( 0, velocity.z );
		mover.Leap( velocity );
		Leaping = true;
		leapEnds = 0.2f;
		energy -= LeapEnergy;
		PlayVoice( "bite_alt" );
	}

	void OnWeaponTag( string tag )
	{
		if ( tag == "attack_end" ) parasiteHeld = false;
		if ( tag != "hit" || parasiteHeld ) return;
		energy = Math.Max( 0, energy - BiteEnergy );
		// BiteLeap checks a 1.2m square with nine box traces, center first, preferring players.
		const float step = 1.2f * MapLoader.Scale / 3;
		System.ReadOnlySpan<int> order = [4, 1, 3, 5, 7, 0, 2, 6, 8];
		PlayVoice( "bite" );
		SceneTraceResult hit = default, center = default;
		IDamageable victim = null;
		var view = ViewRotation;
		var forward = view.Forward.WithZ( 0 ).Normal;
		foreach ( var cell in order )
		{
			var start = EyePosition + view.Left * ((cell % 3 - 1) * step) + view.Up * ((cell / 3 - 1) * step);
			var trace = Scene.Trace.Ray( start, start + view.Forward * Reach ).Size( step )
				.IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
			if ( cell == 4 ) center = trace;
			if ( trace.GameObject?.GetComponentInParent<IDamageable>() is not { } target || GetDisposition( (target as Component)?.GameObject ?? trace.GameObject ) != Disposition.Hostile ) continue;
			if ( Vector3.Dot( trace.EndPosition - EyePosition, forward ) <= 0.1f * MapLoader.Scale ) continue;
			bool player = trace.GameObject.GetComponentInParent<PlayerController>() is not null;
			if ( victim is null || player ) (hit, victim) = (trace, target);
			if ( player ) break;
		}
		if ( victim is null )
		{
			hit = center;
			if ( hit.GameObject?.GetComponentInParent<IDamageable>() is Component candidate && GetDisposition( candidate.GameObject ) == Disposition.Hostile )
				victim = (IDamageable)candidate;
		}
		if ( hit.Hit ) PlayBiteImpact( hit );
		if ( victim is null ) return;
		var npc = victim as BaseNpc;
		bool alive = npc.IsValid() && npc.Health > 0;
		DealDamage( victim, 75, hit.EndPosition, "bite" );
		if ( alive && (!npc.IsValid() || npc.Health <= 0) ) PlayVoice( "bite_kill" );
	}
}
