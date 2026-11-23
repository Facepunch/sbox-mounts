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
			root.AddComponent<SensesLayer>();
			root.AddComponent<AnimationLayer>().EnableFootstepSounds = false;
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
	[Sync( SyncFlags.FromHost )] bool Biting { get; set; }
	[Sync( SyncFlags.FromHost )] bool Leaping { get; set; }
	SkulkMovement mover;
	TimeUntil leapEnds;
	float energy = 100;
	SparkAnimationGraph.Playback world, weapon;
	Action<string> weaponTag;
	TimeUntil repath;

	protected override void OnStart()
	{
		base.OnStart();
		mover = GetComponent<SkulkMovement>();
		standingYaw = runningYaw = WorldRotation.Angles().yaw;
		worldTag = OnWorldTag;
		if ( Networking.IsHost ) PlayVoice( "spawn" );
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			world = new( model.PropGraph, model );
		if ( Networking.IsHost && mount?.GetByPath( "mount://ns2/ns2/models/alien/skulk/skulk_view.model.vmdl" ) is ModelLoader viewModel )
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
			UpdateFlinch();
			energy = Math.Min( 100, energy + Time.Delta * 10 );
			if ( Leaping && leapEnds <= 0 ) Leaping = false;
		}

		UpdateAnimation();
	}

	protected override void Think()
	{
		var target = Senses.VisibleHostiles.FirstOrDefault();
		Animation.SetLookTarget( target );
		if ( !target.IsValid() )
		{
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			Biting = false;
			return;
		}

		var direction = target.WorldPosition - WorldPosition;
		var contact = target.GetComponent<Rigidbody>() is { PhysicsBody.IsValid: true } body
			? body.FindClosestPoint( EyePosition )
			: target.GetComponentInChildren<Collider>()?.FindClosestPoint( EyePosition ) ?? target.WorldPosition;
		var distance = EyePosition.Distance( contact );
		if ( distance <= Reach )
		{
			Movement.Stop();
			if ( !direction.WithZ( 0 ).IsNearlyZero() ) WorldRotation = Rotation.LookAt( direction.WithZ( 0 ) );
			Biting = energy >= BiteEnergy;
			return;
		}

		Biting = false;
		var aim = (CalculateAimPoint( target ) - EyePosition).Normal;
		if ( Animation.IsGrounded && LeapUnlocked && !Leaping && energy > LeapEnergy + 10 && distance is > (5 * MapLoader.Scale) and < (18 * MapLoader.Scale)
			&& (target.GetComponent<PlayerController>().IsValid() || target.GetComponent<BaseNpc>().IsValid())
			&& Vector3.Dot( ViewRotation.Forward, aim ) > 0.95f )
		{
			var velocity = Animation.MoveVelocity * 0.5f;
			velocity += aim.WithZ( 0 ).Normal * (7.6f * MapLoader.Scale);
			velocity.z = (aim.z + 0.5f) * (10.8f * MapLoader.Scale) + Math.Min( 0, velocity.z );
			Movement.Stop();
			mover.Leap( velocity );
			Leaping = true;
			leapEnds = 0.2f;
			energy -= LeapEnergy;
			PlayVoice( "bite_alt" );
			return;
		}

		if ( repath <= 0 )
		{
			Movement.MoveTo( target.WorldPosition, Reach * 0.6f, Movement.RunSpeed, target );
			repath = 0.2f;
		}
	}

	void OnWeaponTag( string tag )
	{
		if ( tag != "hit" ) return;
		energy = Math.Max( 0, energy - BiteEnergy );
		// BiteLeap checks a 1.2m square with nine box traces, center first, preferring players.
		const float step = 1.2f * MapLoader.Scale / 3;
		System.ReadOnlySpan<int> order = [4, 1, 3, 5, 7, 0, 2, 6, 8];
		PlayVoice( "bite" );
		SceneTraceResult hit = default, center = default;
		IDamageable victim = null;
		var view = ViewRotation;
		foreach ( var cell in order )
		{
			var start = EyePosition + view.Right * ((cell % 3 - 1) * step) + view.Up * ((cell / 3 - 1) * step);
			var trace = Scene.Trace.Ray( start, start + view.Forward * Reach ).Size( step )
				.IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
			if ( cell == 4 ) center = trace;
			if ( trace.GameObject?.GetComponentInParent<IDamageable>() is not { } target || GetDisposition( (target as Component)?.GameObject ?? trace.GameObject ) != Disposition.Hostile ) continue;
			if ( Vector3.Dot( trace.HitPosition - EyePosition, view.Forward ) <= 0.1f * MapLoader.Scale ) continue;
			bool player = trace.GameObject.GetComponentInParent<PlayerController>() is not null;
			if ( victim is null || player ) (hit, victim) = (trace, target);
			if ( player ) break;
		}
		if ( victim is null ) hit = center;
		if ( hit.Hit ) PlayBiteImpact( hit );
		if ( victim is null ) return;
		var npc = victim as BaseNpc;
		bool alive = npc.IsValid() && npc.Health > 0;
		var damage = new DamageInfo { Damage = 75, Attacker = GameObject, Weapon = GameObject, Origin = EyePosition, Position = hit.HitPosition };
		damage.Tags.Add( "bite" );
		victim.OnDamage( damage );
		if ( alive && (!npc.IsValid() || npc.Health <= 0) ) PlayVoice( "bite_kill" );
	}
}
