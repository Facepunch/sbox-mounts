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
	internal float RagdollStun => Math.Max( 0f, limpStun );
	float knockoutVoiceDelay;
	class BluntDamage : DamageInfo;
	sealed class StrikeDamage : BluntDamage
	{
		public Vector3 Impulse { get; init; }
		public Vector3 LineDirection { get; init; }
	}

	(Vector3 Point, Vector3 Impulse, Vector3 LineDirection)? deathImpulse;
	float? injuryAge;
	bool itemInjury;
	bool extractionDeath;

	Vector3 DamageImpulse( DamageInfo damage ) => damage switch
	{
		StrikeDamage strike => strike.Impulse,
		ItemWound wound => wound.Impulse,
		ItemImpact impact => impact.Impulse,
		ExtractionDamage extraction => extraction.Impulse,
		ExecutionDamage => WorldRotation.Forward * (1000f * 40f / 120f),
		_ => default
	};

	protected override void OnDamaging( Component.IDamageable.DamageEvent hit )
	{
		base.OnDamaging( hit );
		if ( hit.DamageInfo is not ExecutionDamage ) hit.AcceptedDamage /= Math.Max( 0.00001f, DamageResistance );
		if ( hit.DamageInfo is BluntDamage )
		{
			if ( KnockoutShield > 0 && hit.DamageInfo is not LandingDamage ) hit.AcceptedDamage = 0f;
			// HandleRagdollImpact applies the damage a second time if the startled fighter is still awake.
			if ( hit.DamageInfo is StrikeDamage && startled > 0 && !Unconscious && hit.AcceptedDamage < TemporaryHealth )
				hit.AcceptedDamage *= 2f;
			hit.AcceptedDamage *= PermanentDamageFraction;
		}
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent hit )
	{
		base.OnDamaged( hit );
		if ( hit.DamageInfo is BloodDamage && hit.AcceptedDamage > 0f )
		{
			if ( hit.DamageInfo is not ExtractionDamage ) FlinchFromCut();
			ApplyBloodLoss( hit.AcceptedDamage );
		}
		bool sparringKnockout = Sparring && Health < 1f && hit.AcceptedDamage > 0;
		if ( sparringKnockout )
		{
			Health = MaxHealth;
			BloodLost = 0f;
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
				if ( Sparring )
				{
					recoverableDamage = 0f;
					sparringKnockout = true;
				}
				else if ( !Unconscious )
				{
					Unconscious = true;
					sinceUnconscious = 0f;
					if ( !holdPartner.IsValid() )
					{
						knockoutVoiceDelay = 0.4f;
						ReportSound( GameObject, WorldPosition, HeardSound.Voice, 10f );
					}
				}
			}
		}
		var attacker = hit.Instigator;

		if ( holdPartner.IsValid() && attacker.IsValid() && attacker != holdPartner.GameObject ) ReleaseHold();
		NoticeAttacker( attacker );
		if ( !Downed && hit.DamageInfo is not (BloodDamage or HoldDamage or ExecutionDamage or ItemImpact) ) Speak( "hit" );

		// Ordinary damage costs health. Authored strikes also carry a knockdown impulse.
		if ( hit.DamageInfo is not StrikeDamage && !sparringKnockout ) return;
		if ( !Downed )
			GoLimp();
		else ragdoll.GetOrAddComponent<RagdollDamping>().Reset( ragdoll );

		sinceKnockdown = 0;
		if ( hit.DamageInfo is StrikeDamage )
		{
			blockHealth = 0f;
			limpStun = 0.9f;
		}
		var impulse = DamageImpulse( hit.DamageInfo );
		if ( !impulse.IsNearlyZero() ) ApplyHitImpulse( ragdoll, hit.DamageInfo.Position, impulse, (hit.DamageInfo as StrikeDamage)?.LineDirection ?? default );
	}

	void ReceiveSurpriseSwordHit()
	{
		if ( !Downed )
		{
			GoLimp();
			if ( boundAnimations.FallPose is { Writhe: not null } pose )
				ragdoll.GetOrAddComponent<RagdollInjury>().StartInjury( ragdoll, pose, 0f, false );
		}
		sinceKnockdown = 0f;
		rollDelay = Math.Max( (float)rollDelay, 1f );
	}

	void GoLimp()
	{
		EndLedge();
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
		extractionDeath = death.DamageInfo is ExtractionDamage;
		if ( extractionDeath ) ReportSound( GameObject, WorldPosition, HeardSound.Voice, 10f );
		if ( death.DamageInfo is FallImpactDamage or LandingDamage ) PlayCombatSound( "Data/Sounds/hit/hit_hard.xml", death.DamageInfo.Position );
		// The engine removes the NPC on death, so release both hands before it removes the children.
		while ( LeftHandItem.IsValid() || RightHandItem.IsValid() ) DropHandItem();
		var impulse = DamageImpulse( death.DamageInfo );
		deathImpulse = impulse.IsNearlyZero() ? null : (death.DamageInfo.Position, impulse, (death.DamageInfo as StrikeDamage)?.LineDirection ?? default);
		itemInjury = death.DamageInfo is ItemWound or ItemImpact && !Unconscious;
		injuryAge = death.DamageInfo switch
		{
			ExtractionDamage => null,
			ExecutionDamage => 0f,
			ItemWound or ItemImpact when !Unconscious => Random.Shared.Float( 0f, 12f ),
			BloodDamage when !Unconscious => Random.Shared.Float( 5f, 8f ),
			_ => null
		};
		base.OnDeath( death );
	}

	protected override GameObject CreateRagdoll()
	{
		var corpse = base.CreateRagdoll();
		if ( corpse.IsValid() && corpse.GetComponent<ModelPhysics>() is { } physics )
		{
			var remains = corpse.GetOrAddComponent<CharacterRemains>();
			remains.Teams = Teams;
			remains.StaticCharacter = StaticCharacter;
			remains.Rig = boundAnimations;
			remains.Physics = physics;
			foreach ( var witness in Scene.GetAllComponents<RabbitGuard>() )
			{
				if ( witness.knownCasualties.Remove( GameObject ) ) witness.knownCasualties.Add( corpse );
				if ( witness.chokeVictim == GameObject ) witness.chokeVictim = corpse;
				if ( witness.casualty == GameObject ) witness.casualty = corpse;
				if ( witness.escortTarget == GameObject ) witness.escortTarget = corpse;
			}
			var damping = corpse.GetOrAddComponent<RagdollDamping>();
			damping.Reset( physics );
			if ( extractionDeath ) corpse.GetOrAddComponent<RagdollInjury>().QueueDeathVoice();
			if ( injuryAge is { } age && boundAnimations?.FallPose is { Writhe: not null } pose )
				corpse.GetOrAddComponent<RagdollInjury>().StartInjury( physics, pose, age, itemInjury );
			if ( corpse.GetComponent<SkinnedModelRenderer>() is { } corpseRenderer )
				foreach ( var item in GetComponentsInChildren<LevelItem>().Where( item => item.StuckIn == Renderer ).ToArray() )
				{
					item.GameObject.SetParent( corpse, true );
					item.StuckIn = corpseRenderer;
				}
			if ( deathImpulse is { } hit ) ApplyHitImpulse( physics, hit.Point, hit.Impulse, hit.LineDirection );
		}
		return corpse;
	}

	static void ApplyHitImpulse( ModelPhysics ragdoll, Vector3 point, Vector3 impulse, Vector3 lineDirection = default )
	{
		float Weight( Rigidbody rb )
		{
			var offset = rb.PhysicsBody.MassCenter - point;
			offset -= lineDirection * offset.Dot( lineDirection );
			return rb.Mass / (offset.LengthSquared + 16f);
		}
		float total = ragdoll.Bodies.Where( b => b.Component.IsValid() ).Sum( b => Weight( b.Component ) );
		if ( total <= 0 ) return;
		foreach ( var body in ragdoll.Bodies )
			if ( body.Component.IsValid() ) body.Component.ApplyImpulse( impulse * (Weight( body.Component ) / total) );
	}

	bool TryGetUp( Vector3? blockFacing = null )
	{
		if ( Unconscious || !ragdoll.IsValid() || boundAnimations is null ) return false;
		bool canWakeRoll = !blockFacing.HasValue && boundAnimations.Species != "wolf" && HasCombatTarget && startled <= 0f;
		if ( !blockFacing.HasValue && sinceKnockdown < 1f && !canWakeRoll ) return false;
		Vector3 velocity = default;
		float mass = 0;
		foreach ( var body in ragdoll.Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() ) continue;
			velocity += rb.Velocity * rb.Mass;
			mass += rb.Mass;
		}
		if ( mass <= 0 ) return false;
		velocity /= mass;
		bool wakeRoll = !blockFacing.HasValue && (sinceKnockdown < 1f || velocity.LengthSquared >= 20f * 40f * 40f);
		if ( wakeRoll && !canWakeRoll ) return false;
		if ( CharacterLoader.RagdollCenter( ragdoll ) is not { } center ) return false;
		var extent = MovementCapsule.WorldScale;
		var capsule = new Capsule( MovementCapsule.Start * extent, MovementCapsule.End * extent, MovementCapsule.Radius * extent.x );
		var ground = RecoverySupport( center );
		bool inAir = !ground.HasValue;
		if ( inAir && (wakeRoll || blockFacing.HasValue) )
		{
			if ( wakeRoll ) rollDelay = 1f;
			return false;
		}
		if ( wakeRoll && (sinceKnockdown <= 0.2f || rollDelay > 0f) ) return false;

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
			var support = ground.Value;
			var lift = capsule.Radius * (1f / support.Normal.z - 1f);
			feet = support.Point + Vector3.Up * lift;
			var floor = Scene.Trace.Capsule( capsule, feet + Vector3.Up * 0.5f, support.Point + Vector3.Down )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			if ( !floor.Hit || floor.StartedSolid || floor.Normal.z <= 0.5f ) return false;
			feet = floor.EndPosition;
		}

		var (front, up) = GetTorsoDirections();
		var facing = blockFacing ?? -up.WithZ( 0 );
		CapturePhysicsPose();
		if ( ragdoll.GetComponent<RagdollInjury>() is { } injury ) injury.Enabled = false;
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
				StartRoll( direction, velocity, ground?.Surface?.ResourceName );
			}
			else StartGetUp( blockFacing.HasValue ? BlockFromGround : front.z < 0f ? StandFromFront : StandFromBack );
		}
		secondaryReset = 4f * (4f / 120f);
		awaitingThrowImpact = false;
		throwPartner = null;
		Downed = false;
		return true;
	}

	(Vector3 Point, Vector3 Normal, Surface Surface)? RecoverySupport( Vector3 center )
	{
		const float radius = 40f;
		var below = Scene.Trace.Ray( center, center + Vector3.Down * radius )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "npc", "player" ).Run();
		if ( below.Hit && !below.StartedSolid && below.Normal.z > 0.5f ) return (below.EndPosition, below.Normal, TerrainLoader.ContactSurface( below, below.EndPosition ));

		// CanRoll checks every static contact within a one-metre sphere. A ledge can
		// support the body even when there is no floor directly below its centre.
		(Vector3 Point, Vector3 Normal, Surface Surface)? support = null;
		float closest = radius * radius;
		foreach ( var hit in Scene.Trace.Sphere( radius, center, center ).UseHitPosition().IgnoreDynamic()
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "npc", "player" ).RunAll() )
		{
			var offset = center - hit.HitPosition;
			float distance = offset.LengthSquared;
			if ( distance >= closest || offset.Normal.z <= 0.5f ) continue;
			support = (hit.HitPosition, offset.Normal, TerrainLoader.ContactSurface( hit, hit.HitPosition ));
			closest = distance;
		}
		return support;
	}

	void StartGetUp( string stance )
	{
		activeBlockReaction = false;
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
		int count = 0;
		foreach ( var name in boundAnimations.TorsoBones )
		{
			var bone = Renderer.Model.Bones.GetBone( name );
			if ( TorsoBoneRotation( bone ) is not { } orientation ) continue;
			front += orientation.Up;
			up += orientation.Forward;
			count++;
		}
		return count == 0 ? default : (front / count, up / count);
	}

	Rotation? TorsoBoneRotation( BoneCollection.Bone bone )
	{
		if ( !Downed || !ragdoll.IsValid() )
			return Renderer.TryGetBoneTransform( bone, out var world ) ? world.Rotation : null;

		// WakeUp reads physics directly. Fixed bones inherit their parent's bind-space offset.
		var relative = Rotation.Identity;
		for ( ; bone is not null; bone = bone.Parent )
		{
			foreach ( var part in ragdoll.Bodies )
				if ( part.Bone == bone.Index && part.Component.IsValid() ) return part.Component.WorldRotation * relative;
			relative = bone.LocalTransform.Rotation * relative;
		}
		return null;
	}
}
