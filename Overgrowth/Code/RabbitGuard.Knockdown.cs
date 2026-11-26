using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string StandFromFront = "r_standfromfront", StandFromBack = "r_standfromback2", BlockFromGround = "r_blockfrontfromground";
	internal static bool IsGetUp( string stance ) => stance is StandFromFront or StandFromBack or BlockFromGround;
	internal bool GettingUp => IsGetUp( Stance ) && ReactionPhase < 1f;
	[ConVar( "overgrowth_sparring", ConVarFlags.Replicated, Help = "Overgrowth fighters recover their health on a lethal hit and can keep fighting." )]
	public static bool Sparring { get; set; }
	[Property] public CapsuleCollider MovementCapsule { get; set; }
	[Sync, Change( nameof( OnDownedChanged ) )] internal bool Downed { get; private set; }

	[Sync] bool RecoveredInAir { get; set; }
	[Sync] bool Unconscious { get; set; }
	float recoverableDamage;
	float healthBelowFloor;
	float PermanentHealth => Health - healthBelowFloor;
	float TemporaryHealth
	{
		get => PermanentHealth - recoverableDamage;
		set => recoverableDamage = PermanentHealth - value;
	}

	void RegenerateHealth( float delta )
	{
		blockHealth = Math.Min( TemporaryHealth / MaxHealth, blockHealth + delta * 0.3f );
		recoverableDamage = Math.Max( 0f, recoverableDamage - MaxHealth * 0.05f * delta );
	}

	void SetLivingHealth( float permanent )
	{
		Health = Math.Max( 1f, permanent );
		healthBelowFloor = Health - permanent;
	}
	float StrikeForceScale => MaxHealth > 0f ? 1f - Math.Clamp( TemporaryHealth / MaxHealth, 0f, 1f ) * 0.5f : 1f;
	const float PermanentDamageFraction = 0.4f;
	[Property] public float DamageResistance { get; set; } = 1f;
	[Property] public int KnockoutShield { get; set; }
	ModelPhysics ragdoll;
	TimeSince sinceKnockdown, fallAge, sinceUnconscious;
	float earliestRollAge;

	TimeUntil limpStun;
	internal float RagdollStun => Math.Max( 0f, limpStun );
	float knockoutVoiceDelay;
	class BluntDamage : DamageInfo;
	sealed class StrikeDamage : BluntDamage
	{
		public float SharpDamage { get; init; }
		public Vector3 Impulse { get; init; }
		public Vector3 LineDirection { get; init; }
	}

	(Vector3 Point, Vector3 Impulse, Vector3 LineDirection)? deathImpulse;
	(float Age, bool Item, float Stun, RagdollInjury Previous)? corpseInjury;
	GameObject deathRagdoll;
	bool extractionDeath;
	bool cutThroat;

	Vector3 DamageImpulse( DamageInfo damage ) => damage switch
	{
		StrikeDamage strike => strike.Impulse,
		MeleeCut cut => cut.Contact.Impulse,
		ItemWound wound => wound.Impulse,
		ItemImpact impact => impact.Impulse,
		ExtractionDamage extraction => extraction.Impulse,
		ExecutionDamage => WorldRotation.Forward * (1000f * 40f / 120f),
		_ => default
	};

	protected override void OnDamaging( Component.IDamageable.DamageEvent hit )
	{
		base.OnDamaging( hit );
		if ( hit.DamageInfo is BloodDamage blood ) blood.HealthBeforeHit = PermanentHealth;
		if ( hit.DamageInfo is not ExecutionDamage ) hit.AcceptedDamage /= Math.Max( 0.00001f, DamageResistance );
		if ( hit.DamageInfo is BluntDamage )
		{
			if ( KnockoutShield > 0 && hit.DamageInfo is not LandingDamage ) hit.AcceptedDamage = 0f;
			bool repeat = hit.DamageInfo is StrikeDamage && startled > 0 && !Unconscious && hit.AcceptedDamage < TemporaryHealth;
			if ( hit.DamageInfo is StrikeDamage or FallImpactDamage ) TryImpactHeadDrips( hit.DamageInfo.Damage, repeat ? 6 : 3 );
			if ( repeat )
				hit.AcceptedDamage *= 2f;
			hit.AcceptedDamage *= PermanentDamageFraction;
		}
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent hit )
	{
		bool wasAwake = !Unconscious;
		base.OnDamaged( hit );
		if ( hit.AcceptedDamage > 0f || hit.DamageInfo is BluntDamage or BloodDamage )
			InvestigatePatrolDamage();
		if ( hit.DamageInfo is BloodDamage blood && hit.AcceptedDamage > 0f )
		{
			if ( hit.DamageInfo is not (ExtractionDamage or FireDamage) ) FlinchFromCut();
			ApplyBloodLoss( hit.AcceptedDamage, blood );
		}
		if ( hit.DamageInfo is BluntDamage )
		{
			if ( PermanentHealth <= 0f ) Health = 0f;
			else if ( Health < 1f || healthBelowFloor > 0f ) SetLivingHealth( PermanentHealth );
		}
		bool sparringKnockout = Sparring && Health < 1f && hit.AcceptedDamage > 0;
		if ( sparringKnockout )
		{
			Health = MaxHealth;
			healthBelowFloor = 0f;
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
			recoverableDamage += hit.AcceptedDamage * ((1f - PermanentDamageFraction) / PermanentDamageFraction);
			if ( hit.DamageInfo is StrikeDamage ) recoverableDamage = Math.Min( PermanentHealth, recoverableDamage );
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
					UpdateCombatGroup();
					sinceUnconscious = 0f;
					if ( !IsTethered )
					{
						knockoutVoiceDelay = 0.4f;
						ReportBodySound( HeardSound.Voice, 10f );
					}
				}
			}
		}
		var attacker = hit.Instigator;

		if ( holdPartner.IsValid() && attacker.IsValid() && attacker != holdPartner.GameObject ) ReleaseHold();
		NoticeAttacker( attacker );
		bool hitVoice = hit.DamageInfo is StrikeDamage ? wasAwake : !Downed && hit.DamageInfo is not (BloodDamage or HoldDamage or ExecutionDamage or ItemImpact or WaterDamage or LandingDamage);
		if ( hitVoice ) Speak( "hit" );

		// Ordinary damage costs health. Authored strikes also carry a knockdown impulse.
		if ( hit.DamageInfo is not StrikeDamage && !sparringKnockout ) return;
		if ( !Downed )
			GoLimp();
		else ragdoll.GetComponent<RagdollDamping>()?.Refresh();

		RestartRecovery();
		if ( hit.DamageInfo is StrikeDamage )
		{
			blockHealth = 0f;
			limpStun = 0.9f;
		}
		var impulse = DamageImpulse( hit.DamageInfo );
		if ( !impulse.IsNearlyZero() ) ApplyHitImpulse( ragdoll, hit.DamageInfo.Position, impulse, (hit.DamageInfo as StrikeDamage)?.LineDirection ?? default );
		if ( wasAwake && Unconscious && hit.DamageInfo is StrikeDamage { SharpDamage: > 0f } && boundAnimations?.FallPose is { Writhe: not null } pose )
			ragdoll.GetOrAddComponent<RagdollInjury>().StartInjury( ragdoll, pose, Random.Shared.Float( 5f, 8f ), false );
	}

	void ReceiveSurpriseSwordHit()
	{
		EnterInjuredRagdoll();
		RestartRecovery( 1f );
	}

	void EnterInjuredRagdoll()
	{
		if ( Downed ) return;
		GoLimp();
		if ( boundAnimations?.FallPose is { Writhe: not null } pose )
			ragdoll.GetOrAddComponent<RagdollInjury>().StartInjury( ragdoll, pose, 0f, false );
	}

	void RestartRecovery( float earliestRoll = 0.2f )
	{
		sinceKnockdown = 0f;
		earliestRollAge = earliestRoll;
	}

	bool passiveRagdoll;

	void GoLimp() => EnterRagdoll( passive: false );

	void EnterRagdoll( bool passive )
	{
		RestartRecovery();
		if ( Downed && ragdoll is { Active: true } )
			ragdoll.GetComponent<RagdollDamping>()?.Refresh();
		else
			BeginRagdoll( passive );

		ReleaseTether();
	}

	void BeginRagdoll( bool passive )
	{
		EndLedge();
		passiveRagdoll = passive;
		fallAge = sinceRagdollImpact = 0;
		limpStun = 0f;
		displayingRoll = false;
		RecoveredInAir = false;
		rollAfterRagdollDelay = Random.Shared.Float( 0.1f, 2f - GameDifficulty );
		RollProgress = 1f;
		EndRecovery();
		// Older saved instances predate the explicit prefab reference.
		MovementCapsule ??= GetComponentInChildren<CapsuleCollider>();
		Movement.Stop();
		combatVelocity = default;
		Movement.Enabled = false;
		UseAirHull( false );
		MovementCapsule.Enabled = false;
		if ( strike is { AsLayer: false } ) strike = null;
		// Copy the live pose before letting the physics skeleton drive it.
		ragdoll = Renderer.GetOrAddComponent<ModelPhysics>( false );
		ragdoll.Renderer = Renderer;
		ragdoll.Model = Renderer.Model;
		ragdoll.IgnoreRoot = true;
		ragdoll.GetOrAddComponent<RagdollHands>().Begin( Renderer );
		ragdoll.CopyBonesFrom( Renderer, true );
		ReleaseProceduralPose();
		ragdoll.Enabled = true;
		InheritPoseMotion( ragdoll );
		motionSamples = 0;
		ragdoll.GetOrAddComponent<RagdollDamping>().Reset( ragdoll );
		ragdoll.GetOrAddComponent<RabbitBodyContact>().Fighter = this;
		foreach ( var part in ragdoll.Bodies )
		{
			if ( !part.Component.IsValid() ) continue;
			part.Component.EnableImpactDamage = false;
			part.Component.CollisionEventsEnabled = true;
		}
		Downed = true;
	}

	void OnDownedChanged( bool wasDown, bool isDown )
	{
		if ( !wasDown || isDown ) return;
		armsActive = earsActive = plantedLegs = false;
		foreach ( var ear in earStates )
		{
			ear.Twist = ear.TargetTwist = 0f;
			ear.NextTwist = 0f;
		}
		if ( tailMotion is not null ) tailMotion.Initialized = false;
		ClearFootContacts();
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		UpdateCombatGroup();
		extractionDeath = death.DamageInfo is ExtractionDamage;
		cutThroat = death.DamageInfo is ExecutionDamage;
		if ( death.DamageInfo is FireDamage ) PlayCombatSound( "Data/Sounds/voice/animal2/voice_bunny_death.xml", CharacterCenter( GameObject ) );
		if ( extractionDeath || death.DamageInfo is FireDamage ) ReportBodySound( HeardSound.Voice, 10f );
		// The engine removes the NPC on death, so release both hands before it removes the children.
		while ( LeftHandItem.IsValid() || RightHandItem.IsValid() ) DropHandItem();
		var impulse = DamageImpulse( death.DamageInfo );
		var line = death.DamageInfo is MeleeCut cut ? cut.Contact.Line : (death.DamageInfo as StrikeDamage)?.LineDirection ?? default;
		deathImpulse = impulse.IsNearlyZero() ? null : (death.DamageInfo.Position, impulse, line);
		float sharp = death.DamageInfo switch
		{
			StrikeDamage strike => strike.SharpDamage,
			MeleeCut melee => melee.SharpDamage,
			_ => 0f
		};
		bool awakeAtImpact = death.DamageInfo is MeleeCut wound ? wound.AwakeAtImpact : !Unconscious;
		float stun = death.DamageInfo is StrikeDamage or MeleeCut { AppliesImpact: true } ? (sharp >= 0.5f ? 0.9f : 0f) : limpStun;
		var previous = ragdoll?.GetComponent<RagdollInjury>();
		bool bluntFinisher = death.DamageInfo is StrikeDamage { SharpDamage: 0f } && previous is { Active: true };
		if ( bluntFinisher || death.DamageInfo is FallImpactDamage or LandingDamage )
			PlayCombatSound( "Data/Sounds/hit/hit_hard.xml", CharacterCenter( GameObject ) );
		corpseInjury = death.DamageInfo switch
		{
			ExtractionDamage => null,
			ExecutionDamage or FireDamage => (0f, false, 0f, null),
			ItemWound or ItemImpact when !Unconscious => (Random.Shared.Float( 0f, 12f ), true, 0.9f, null),
			_ when sharp > 0f && awakeAtImpact => (Random.Shared.Float( 5f, 8f ), false, stun, null),
			_ when sharp > 0f && previous is { Active: true } => (previous.Age + sharp * 10f, false, stun, previous),
			_ => null
		};
		if ( death.DamageInfo is BluntDamage && !Unconscious && !IsTethered ) ReportBodySound( HeardSound.Voice, 10f );
		base.OnDeath( death );
		if ( deathRagdoll.IsValid() && awakeAtImpact && (death.DamageInfo is StrikeDamage or MeleeCut { AppliesImpact: true }) && !bluntFinisher )
			deathRagdoll.GetOrAddComponent<RagdollInjury>().Speak( "hit" );
	}

	protected override GameObject CreateRagdoll()
	{
		deathRagdoll = base.CreateRagdoll();
		if ( deathRagdoll.IsValid() && deathRagdoll.GetComponent<ModelPhysics>() is { } physics )
			ConfigureCorpse( deathRagdoll, physics );
		return deathRagdoll;
	}

	void ConfigureCorpse( GameObject corpse, ModelPhysics physics )
	{
		var remains = corpse.GetOrAddComponent<CharacterRemains>();
		remains.Teams = Teams;
		remains.StaticCharacter = StaticCharacter;
		remains.Physics = physics;
		var hitboxes = corpse.GetOrAddComponent<ModelHitboxes>();
		hitboxes.Renderer = corpse.GetComponent<SkinnedModelRenderer>();
		hitboxes.Target = corpse;
		remains.Burning = Burning;
		remains.FireParticles = fireParticles;
		fireParticles = default;
		remains.FireRig = boundAnimations;
		remains.BodySounds = bodySounds;
		if ( !zoneKilled )
		{
			physics.GetOrAddComponent<RabbitBodyContact>().Remains = remains;
			foreach ( var part in physics.Bodies )
			{
				if ( !part.Component.IsValid() ) continue;
				part.Component.CollisionEventsEnabled = true;
			}
		}
		Renderer.GetComponent<CutMarks>()?.TransferTo( corpse );
		Renderer.GetComponent<CharacterProportions>()?.CopyTo( corpse );
		foreach ( var witness in Scene.GetAllComponents<RabbitGuard>() )
			witness.FollowCorpse( GameObject, corpse );
		corpse.GetOrAddComponent<RagdollHands>().Begin( Renderer, Downed ? Renderer.GetComponent<RagdollHands>()?.Current : null );
		var damping = corpse.GetOrAddComponent<RagdollDamping>();
		damping.Reset( physics );
		if ( Downed && ragdoll is { Active: true } )
		{
			var liveBodies = ragdoll.Bodies.ToDictionary( b => b.Bone, b => b.Component );
			foreach ( var part in physics.Bodies )
			{
				if ( !part.Component.IsValid() || !liveBodies.TryGetValue( part.Bone, out var live ) || !live.IsValid() ) continue;
				// Carry the current physics pose across death, including the point held by a dragger.
				part.Component.WorldTransform = live.PhysicsBody.Transform.WithScale( live.WorldScale );
				part.Component.Velocity = live.Velocity;
				part.Component.AngularVelocity = live.AngularVelocity;
				TransferBodyDrag( remains, live, part.Component );
			}
		}
		else InheritPoseMotion( physics );
		if ( extractionDeath ) corpse.GetOrAddComponent<RagdollInjury>().QueueDeathVoice();
		if ( corpseInjury is { } injury && boundAnimations?.FallPose is { Writhe: not null } pose )
			corpse.GetOrAddComponent<RagdollInjury>().StartInjury( physics, pose, injury.Age, injury.Item, injury.Previous, injury.Stun );
		if ( corpse.GetComponent<SkinnedModelRenderer>() is { } corpseRenderer )
			foreach ( var item in GetComponentsInChildren<LevelItem>().Where( item => item.StuckIn == Renderer || item.Holder == Renderer ).ToArray() )
			{
				item.GameObject.SetParent( corpse, true );
				if ( item.StuckIn == Renderer ) item.StuckIn = corpseRenderer;
				if ( item.Holder == Renderer ) item.Holder = corpseRenderer;
			}
		if ( cutThroat ) corpse.GetOrAddComponent<ArterialBleeding>().Begin( physics );
		if ( deathImpulse is { } hit ) ApplyHitImpulse( physics, hit.Point, hit.Impulse, hit.LineDirection );
	}

	internal static void ApplyHitImpulse( ModelPhysics ragdoll, Vector3 point, Vector3 impulse, Vector3 lineDirection = default )
	{
		float Weight( Rigidbody rb )
		{
			var offset = rb.WorldPosition - point;
			offset -= lineDirection * offset.Dot( lineDirection );
			return rb.Mass / (offset.LengthSquared + 16f);
		}
		float total = ragdoll.Bodies.Where( b => b.Component.IsValid() ).Sum( b => Weight( b.Component ) );
		if ( total <= 0 || CharacterLoader.FromModel( ragdoll.Model ) is not { } character ) return;
		foreach ( var body in ragdoll.Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() ) continue;
			var lever = character.RagdollForceLever( body.Bone ) * rb.WorldScale;
			rb.ApplyImpulseAt( rb.PhysicsBody.MassCenter + lever, impulse * (Weight( rb ) / total) );
		}
	}

	bool TryGetUp( Vector3? blockFacing = null )
	{
		if ( Unconscious || !ragdoll.IsValid() || boundAnimations is null ) return false;
		if ( CharacterLoader.RagdollVelocity( ragdoll ) is not { } velocity ) return false;
		bool wakeRoll = !blockFacing.HasValue && (sinceKnockdown < 1f || velocity.LengthSquared >= 20f * 40f * 40f);
		if ( wakeRoll && boundAnimations.Species == "wolf" ) return false;
		if ( CharacterLoader.RagdollCenter( ragdoll ) is not { } center ) return false;
		var extent = MovementCapsule.WorldScale;
		var capsule = new Capsule( MovementCapsule.Start * extent, MovementCapsule.End * extent, MovementCapsule.Radius * extent.x );
		var ground = RecoverySupport( center );
		bool inAir = !ground.HasValue;
		if ( inAir && (wakeRoll || blockFacing.HasValue) )
		{
			if ( wakeRoll ) rollAfterRagdollDelay = Math.Max( rollAfterRagdollDelay, (float)sinceKnockdown + 1f );
			return false;
		}
		if ( wakeRoll && (!CombatAllowed || !Sandbox.Npcs.NpcConVars.Enabled || !HasAttackGoal || startled > 0f ||
			sinceKnockdown < earliestRollAge || sinceKnockdown <= rollAfterRagdollDelay) ) return false;

		// On a slope the bottom sphere touches uphill of the point directly below its center.
		var feet = center - Vector3.Up * (0.45f * 40f);
		if ( inAir )
		{
			// The loose bodies can fit in places where the standing collider cannot.
			var clearance = MovementTrace( Scene.Trace.Capsule( capsule, feet, feet ) ).WithoutTags( "trigger", CharacterCollisionRules.FighterHull ).Run();
			if ( clearance.StartedSolid || clearance.Hit ) return false;
		}
		else
		{
			var support = ground.Value;
			var lift = capsule.Radius * (1f / support.Normal.z - 1f);
			feet = support.Point + Vector3.Up * lift;
			var probe = MovementTrace( Scene.Trace ).WithoutTags( "trigger", CharacterCollisionRules.FighterHull );
			var start = feet + Vector3.Up * 0.5f;
			var end = support.Point + Vector3.Down;
			var floor = probe.Capsule( capsule, start, end ).Run();
			// A compound world collider may expose the second wall of a corner only after the first correction.
			for ( int pass = 0; floor.StartedSolid && pass < 4; pass++ )
			{
				var axis = new Line( start + capsule.CenterA, start + capsule.CenterB );
				var shift = Vector3.Zero;
				var contacts = probe.Capsule( capsule, start, start ).UseHitPosition().RunAll();
				foreach ( var separation in contacts.Select( hit => axis.ClosestPoint( hit.HitPosition ) - hit.HitPosition ).OrderBy( offset => offset.LengthSquared ) )
				{
					var distance = separation.Length;
					if ( distance <= 0f || distance >= capsule.Radius ) continue;
					var normal = separation / distance;
					var penetration = capsule.Radius - distance - shift.Dot( normal );
					if ( penetration > 0f ) shift += normal * (penetration + 0.03125f);
				}
				if ( shift.IsNearlyZero() ) break;
				start += shift;
				end += shift.WithZ( 0f );
				floor = probe.Capsule( capsule, start, end ).Run();
			}
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
		rollAngle = 1f;
		if ( !inAir && !wakeRoll && !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
		MovementCapsule.Enabled = true;
		Movement.Enabled = true;
		if ( inAir )
		{
			RecoveredInAir = true;
			Stance = Locomotion;
			duckAmount = CrouchAmount = groundCrouchTarget = 1f;
			duckVelocity = 0f;
			ReactionPhase = 1f;
			Launch( velocity );
			BeginRecovery( 4f );
		}
		else
		{
			if ( GetComponent<NavMeshAgent>( true ) is { } agent )
			{
				agent.Enabled = true;
				agent.SetAgentPosition( WorldPosition );
			}
			if ( wakeRoll )
			{
				var direction = CombatApproach( visible ? TargetPosition( enemy ) : lastSeen, out _ );
				var flat = velocity.WithZ( 0f );
				if ( flat.LengthSquared > 40f * 40f ) direction = (direction * 0.9f + flat.Normal).Normal;
				StartRoll( direction, velocity, ground?.Surface?.ResourceName );
			}
			else StartGetUp( blockFacing.HasValue ? BlockFromGround : front.z < 0f ? StandFromFront : StandFromBack );
		}
		throwPartner = null;
		if ( Sparring ) zoneKilled = false;
		Downed = false;
		return true;
	}

	(Vector3 Point, Vector3 Normal, Surface Surface)? RecoverySupport( Vector3 center )
	{
		const float radius = 40f;
		var trace = Scene.Trace.IgnoreDynamic().IgnoreKeyframed().WithoutTags( "trigger" );
		var below = trace.Ray( center, center + Vector3.Down * radius ).Run();
		if ( below.Hit && !below.StartedSolid && below.Normal.z > 0.5f ) return (below.EndPosition, below.Normal, TerrainLoader.ContactSurface( below, below.EndPosition ));

		(Vector3 Point, Vector3 Normal, Surface Surface)? support = null;
		float closest = radius * radius;
		foreach ( var hit in trace.Sphere( radius, center, center ).UseHitPosition().RunAll() )
		{
			var offset = center - hit.HitPosition;
			float distance = offset.LengthSquared;
			if ( distance >= closest || offset.Normal.z <= 0.5f ) continue;
			support = (hit.HitPosition, offset.Normal, TerrainLoader.ContactSurface( hit, hit.HitPosition ));
			closest = distance;
		}
		return support;
	}

	float getUpEndTime;

	void StartGetUp( string stance, bool interrupt = false )
	{
		activeBlockReaction = false;
		MirroredAction = MirroredStance = LeftHanded;
		duckAmount = 1f;
		groundCrouchTarget = 0f;
		duckVelocity = 0f;
		if ( interrupt && Stance == stance ) return;
		Stance = stance;
		BeginRecovery( 1f );
		Renderer.Animation.Parameters.Set( "stance", Stance );
		Renderer.Animation.Parameters.Set( "reaction_phase", 0f );
		playing = Stance;
		reactionDuration = boundAnimations.Clips[Stance].Duration;
		getUpEndTime = interrupt ? 1f / 200f : reactionDuration;
		ReactionPhase = 0;
	}

	(Vector3 Front, Vector3 Up) GetTorsoDirections()
	{
		return CharacterLoader.AverageChainFrame( Renderer, "torso" ) is { } torso ? (torso.Up, torso.Forward) : default;
	}
}
