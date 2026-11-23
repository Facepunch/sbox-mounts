using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string StandFromFront = "r_standfromfront", StandFromBack = "r_standfromback2", BlockFromGround = "r_blockfrontfromground";
	internal static bool IsGetUp( string stance ) => stance is StandFromFront or StandFromBack or BlockFromGround;
	[ConVar( "overgrowth_sparring", ConVarFlags.Replicated, Help = "Overgrowth fighters recover their health on a lethal hit and can keep fighting." )]
	public static bool Sparring { get; set; }
	[Property] public CapsuleCollider MovementCapsule { get; set; }
	[Sync] bool Downed { get; set; }
	[Sync] bool RecoveredInAir { get; set; }
	[Sync] bool Unconscious { get; set; }
	float recoverableDamage;
	float TemporaryHealth => Math.Max( 0f, Health - recoverableDamage );
	const float PermanentDamageFraction = 0.4f;
	[Property] public float DamageResistance { get; set; } = 1f;
	[Property] public int KnockoutShield { get; set; }
	ModelPhysics ragdoll;
	TimeSince sinceKnockdown, fallAge, sinceUnconscious;
	TimeUntil limpStun;
	class BluntDamage : DamageInfo;
	sealed class StrikeDamage : BluntDamage
	{
		public Vector3 Impulse { get; init; }
	}

	StrikeDamage lethalBlow;

	protected override void OnDamaging( Component.IDamageable.DamageEvent hit )
	{
		base.OnDamaging( hit );
		hit.AcceptedDamage /= Math.Max( 0.00001f, DamageResistance );
		if ( hit.DamageInfo is BluntDamage )
		{
			if ( KnockoutShield > 0 ) hit.AcceptedDamage = 0f;
			// HandleRagdollImpact applies the damage a second time if the startled fighter is still awake.
			if ( hit.DamageInfo is StrikeDamage && startled > 0 && !Unconscious && hit.AcceptedDamage < TemporaryHealth )
				hit.AcceptedDamage *= 2f;
			hit.AcceptedDamage *= PermanentDamageFraction;
		}
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent hit )
	{
		base.OnDamaged( hit );
		bool sparringKnockout = Sparring && Health < 1f && hit.AcceptedDamage > 0;
		if ( sparringKnockout )
		{
			Health = MaxHealth;
			recoverableDamage = 0f;
			Unconscious = false;
		}
		if ( Health < 1f ) return;
		if ( !Renderer.IsValid() ) return;
		if ( hit.AcceptedDamage <= 0 && !(hit.DamageInfo is StrikeDamage strikeHit && (KnockoutShield > 0 || strikeHit.Damage == 0f)) ) return;
		if ( hit.DamageInfo is StrikeDamage ) KnockoutShield = Math.Max( 0, KnockoutShield - 1 );
		if ( hit.DamageInfo is BluntDamage && !sparringKnockout )
		{
			recoverableDamage = Math.Min( Health, recoverableDamage + hit.AcceptedDamage * ((1f - PermanentDamageFraction) / PermanentDamageFraction) );
			if ( TemporaryHealth <= 0f )
			{
				if ( Sparring ) recoverableDamage = 0f;
				else if ( !Unconscious )
				{
					Unconscious = true;
					sinceUnconscious = 0f;
				}
			}
		}
		var attacker = hit.Instigator;

		NoticeAttacker( attacker );
		if ( !Downed ) Speak( "hit" );

		// Ordinary damage costs health. Authored strikes also carry a knockdown impulse.
		if ( hit.DamageInfo is not StrikeDamage && !sparringKnockout ) return;
		if ( !Downed )
			GoLimp();
		else ragdoll.GetOrAddComponent<RagdollDamping>().Reset( ragdoll );

		sinceKnockdown = 0;
		if ( hit.DamageInfo is StrikeDamage blow )
		{
			limpStun = 0.9f;
			ApplyHitImpulse( ragdoll, blow.Position, blow.Impulse );
		}
	}

	void GoLimp()
	{
		sinceKnockdown = fallAge = sinceRagdollImpact = 0;
		limpStun = 0f;
		displayingRoll = false;
		RecoveredInAir = false;
		rollDelay = Random.Shared.Float( 0.1f, 2f - GameDifficulty );
		RollProgress = 1f;
		EndRecovery();
		// Older saved instances predate the explicit prefab reference.
		MovementCapsule ??= GetComponentInChildren<CapsuleCollider>();
		Movement.Stop();
		blockVelocity = default;
		Movement.Enabled = false;
		MovementCapsule.Enabled = false;
		strike = null;
		blockHealth = 0;
		// Copy the live pose before letting the physics skeleton drive it.
		ragdoll = Renderer.GetOrAddComponent<ModelPhysics>( false );
		ragdoll.Renderer = Renderer;
		ragdoll.Model = Renderer.Model;
		ragdoll.IgnoreRoot = true;
		ragdoll.CopyBonesFrom( Renderer, true );
		ReleaseProceduralPose();
		ragdoll.Enabled = true;
		ragdoll.GetOrAddComponent<RagdollDamping>().Reset( ragdoll );
		foreach ( var part in ragdoll.Bodies )
		{
			if ( !part.Component.IsValid() ) continue;
			part.Component.EnableImpactDamage = false;
			part.Component.CollisionEventsEnabled = true;
			var listener = part.Component.GetOrAddComponent<RabbitBodyContact>();
			listener.Fighter = this;
			var head = Renderer.Model.Bones.GetBone( boundAnimations.HeadBone );
			for ( int i = 0; head is not null && i < boundAnimations.Head.ChainLength; i++, head = head.Parent )
				if ( part.Bone == head.Index ) listener.Head = true;
		}
		Downed = true;
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		if ( death.DamageInfo is FallImpactDamage ) PlayCombatSound( "Data/Sounds/hit/hit_hard.xml", death.DamageInfo.Position );
		lethalBlow = death.DamageInfo as StrikeDamage;
		base.OnDeath( death );
	}

	protected override GameObject CreateRagdoll()
	{
		var corpse = base.CreateRagdoll();
		if ( corpse.IsValid() && corpse.GetComponent<ModelPhysics>() is { } physics )
		{
			corpse.GetOrAddComponent<RagdollDamping>().Reset( physics );
			if ( lethalBlow is not null ) ApplyHitImpulse( physics, lethalBlow.Position, lethalBlow.Impulse );
		}
		return corpse;
	}

	static void ApplyHitImpulse( ModelPhysics ragdoll, Vector3 point, Vector3 impulse )
	{
		float Weight( Rigidbody rb ) => rb.Mass / (rb.PhysicsBody.MassCenter.DistanceSquared( point ) + 16f);
		float total = ragdoll.Bodies.Where( b => b.Component.IsValid() ).Sum( b => Weight( b.Component ) );
		if ( total <= 0 ) return;
		foreach ( var body in ragdoll.Bodies )
			if ( body.Component.IsValid() ) body.Component.ApplyImpulse( impulse * (Weight( body.Component ) / total) );
	}

	bool TryGetUp( Vector3? blockFacing = null )
	{
		if ( Unconscious || !ragdoll.IsValid() || boundAnimations is null ) return false;
		bool canWakeRoll = !blockFacing.HasValue && boundAnimations.Species != "wolf" && CanAttack( enemy ) && startled <= 0f;
		if ( !blockFacing.HasValue && sinceKnockdown < 1f && (!canWakeRoll || sinceKnockdown <= 0.2f || rollDelay > 0f) ) return false;
		Vector3 center = default, velocity = default;
		float mass = 0;
		int count = 0;
		foreach ( var body in ragdoll.Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() ) continue;
			center += rb.WorldPosition;
			velocity += rb.Velocity * rb.Mass;
			mass += rb.Mass;
			count++;
		}
		if ( count == 0 || mass <= 0 ) return false;
		velocity /= mass;
		bool wakeRoll = !blockFacing.HasValue && (sinceKnockdown < 1f || velocity.LengthSquared >= 20f * 40f * 40f);
		if ( wakeRoll && (!canWakeRoll || rollDelay > 0f) ) return false;
		center /= count;
		var extent = MovementCapsule.WorldScale;
		var capsule = new Capsule( MovementCapsule.Start * extent, MovementCapsule.End * extent, MovementCapsule.Radius * extent.x );
		var ground = Scene.Trace.Ray( center, center + Vector3.Down * 40f )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		bool inAir = !ground.Hit;
		if ( ground.StartedSolid || !inAir && ground.Normal.z <= 0.5f || inAir && (wakeRoll || blockFacing.HasValue) )
		{
			if ( wakeRoll ) rollDelay = 1f;
			return false;
		}

		// On a slope the bottom sphere touches uphill of the point directly below its center.
		var feet = center - Vector3.Up * (0.45f * 40f);
		if ( inAir )
		{
			// The loose bodies can fit in places where the standing collider cannot.
			var clearance = Scene.Trace.Capsule( capsule, feet, feet ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			if ( clearance.StartedSolid || clearance.Hit ) return false;
		}
		else
		{
			var lift = capsule.Radius * (1f / ground.Normal.z - 1f);
			feet = ground.EndPosition + Vector3.Up * lift;
			var floor = Scene.Trace.Capsule( capsule, feet + Vector3.Up * 0.5f, feet + Vector3.Down )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			if ( !floor.Hit || floor.StartedSolid || floor.Normal.z <= 0.5f ) return false;
			feet = floor.EndPosition;
		}

		var (front, up) = GetTorsoDirections();
		var facing = blockFacing ?? -up.WithZ( 0 );
		CapturePhysicsPose();
		ragdoll.Enabled = false;
		Renderer.ClearPhysicsBones();
		WorldPosition = feet;
		if ( !inAir && !wakeRoll && !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
		MovementCapsule.Enabled = true;
		Movement.Enabled = true;
		if ( inAir )
		{
			RecoveredInAir = true;
			Stance = Locomotion;
			duckAmount = CrouchAmount = 1f;
			duckVelocity = 0f;
			ReactionPhase = 1f;
			Launch( velocity );
			BeginRecovery( 4f );
		}
		else
		{
			var agent = GetComponent<NavMeshAgent>( true );
			agent.Enabled = true;
			agent.SetAgentPosition( WorldPosition );
			if ( wakeRoll )
			{
				var direction = CombatApproach( visible ? TargetPosition( enemy ) : lastSeen, out _ );
				var flat = velocity.WithZ( 0f );
				if ( flat.LengthSquared > 40f * 40f ) direction = (direction * 0.9f + flat.Normal).Normal;
				StartRoll( direction, velocity, ground.Surface?.ResourceName );
			}
			else StartGetUp( blockFacing.HasValue ? BlockFromGround : front.z < 0f ? StandFromFront : StandFromBack );
		}
		secondaryReset = 4f * (4f / 120f);
		awaitingThrowImpact = false;
		throwPartner = null;
		Downed = false;
		return true;
	}

	void StartGetUp( string stance )
	{
		MirroredAction = MirroredStance = LeftHanded;
		duckAmount = 1f;
		duckVelocity = 0f;
		Stance = stance;
		BeginRecovery( 1f );
		Renderer.Animation.Parameters.Set( "stance", Stance );
		Renderer.Animation.Parameters.Set( "reaction_phase", 0f );
		playing = Stance;
		reactionDuration = boundAnimations.Clips[Stance].Duration;
		if ( Movement is Sandbox.Npcs.Layers.NavigationLayer navigation )
			navigation.HoldMovement( reactionDuration );
		ReactionPhase = 0;
	}

	(Vector3 Front, Vector3 Up) GetTorsoDirections()
	{
		Vector3 front = default, up = default;
		foreach ( var name in boundAnimations.TorsoBones )
		{
			if ( !Renderer.TryGetBoneTransform( name, out var bone ) ) continue;
			front += bone.Rotation.Up;
			up += bone.Rotation.Forward;
		}
		return (front / boundAnimations.TorsoBones.Length, up / boundAnimations.TorsoBones.Length);
	}
}
