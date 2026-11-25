using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace Overgrowth;

[Library( "overgrowth_turner" )]
public sealed partial class RabbitGuard : BaseNpc, IHotloadManaged
{
	internal const string Idle = "r_idle", Watch = "r_dialogue_shade", Ready = "ready_idle", Threat = "r_combatidlethreat", Locomotion = "movement";
	internal const string Sweep = "r_sweep", SoccerKick = "r_soccerkick";
	internal const string FreeAir = "jump", Roll = "roll";
	[Property, Range( 0.01f, 100f )] public float MovementSpeed { get; set; } = 1f;
	float CharacterScale => boundAnimations.CharacterScale * WorldScale.z;
	internal float GroundRunSpeed => 8f * 40f * Math.Clamp( MovementSpeed, 0.01f, 100f );
	internal const string Kick = "r_frontkick", Knee = "r_kneestrike", Jab = "r_jab", Roundhouse = "r_roundhouse";

	[Sync] public int AlertStage { get; private set; }
	[Property] public bool InvisibleWhenStationary { get; set; }
	[Property] public bool NoLookAround { get; set; }
	[Property, Sync] public string UnarmedStanceOverride { get; set; }
	[Property, Sync] public string Species { get; private set; }
	[Property] public bool StaticCharacter { get; set; }
	[Property] public bool AlwaysUnaware { get; set; }
	const float NoticeThreshold = 1f;
	float awakeTime;
	TimeUntil nextVisionCheck;
	float enemySeen;
	string playing;
	[Sync] string Stance { get; set; } = Idle;
	internal bool InLocomotion => Stance == Locomotion;
	RabbitGuardAnimations boundAnimations;
	Model boundModel;
	LevelItem equippedForAnimation;
	string boundStanceOverride;
	string boundPatrolGroup;

	internal void SetSpecies( string species )
	{
		if ( species is not ("rabbit" or "cat" or "rat" or "dog" or "wolf") ) return;
		Species = species;
		var agent = GetComponent<NavMeshAgent>( true );
		// BrainSpeciesUpdate gives rabbits access to off-mesh routes.
		agent.AllowAllLinkTypes = species == "rabbit";
		if ( !agent.AllowAllLinkTypes ) agent.AllowedLinkTypes.Clear();
		CausesFearOnSight = species == "wolf";
		NeverAfraidOnSight = species is "wolf" or "dog";
	}

	void IHotloadManaged.Created( IReadOnlyDictionary<string, object> state )
	{
		boundAnimations = null;
		if ( MovementCapsule.IsValid() ) MovementCapsule.Tags.Add( CharacterCollisionRules.FighterHull );
		strike = null;
	}

	bool BindAnimations()
	{
		var equipped = PrimaryItem.IsValid() ? PrimaryItem : null;
		var patrolGroup = Stance == PatrolIdle && IsPatrolGroup ? PatrolAnimation : boundPatrolGroup;
		if ( patrolGroup != boundPatrolGroup || boundAnimations is null || boundModel != Renderer.Model || Renderer.Animation.Graph is null
			|| boundStanceOverride != UnarmedStanceOverride || Species is not null && boundAnimations.Species != Species
			|| equippedForAnimation != equipped && !IsThrowingItem )
		{
			if ( CharacterLoader.FromModel( Renderer.Model ) is not { } loader || loader.GetGuardAnimations( equipped?.GetComponent<ModelRenderer>()?.Model, UnarmedStanceOverride, Species, patrolGroup ) is not { } ready ) return false;
			if ( !Downed ) Renderer.ClearPhysicsBones();
			equippedForAnimation = equipped;
			boundStanceOverride = UnarmedStanceOverride;
			boundPatrolGroup = patrolGroup;
			if ( boundModel != Renderer.Model )
			{
				boundModel = Renderer.Model;
				secondaryReset = 4f * (4f / 120f);
			}
			boundAnimations = ready;
			strike = null;
			if ( Networking.IsHost && GetComponent<Rigidbody>() is { } body ) body.GravityScale = FallGravityScale( Scene.PhysicsWorld );
			motionSamples = 0;
			leanBones = null;
			Renderer.Animation.Graph = ready.Graph;
			Renderer.Animation.Parameters.Set( "tall_coord", 1f, persist: true );
			connectedPatrolAnimation = null;
			playing = null;
		}
		return true;
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && NpcConVars.Enabled && Health > 0f && !Unconscious )
		{
			awakeTime += Time.Delta;
			if ( !StaticCharacter ) suspicion = Math.Max( 0f, suspicion - Time.Delta * 0.1f );
		}
		if ( !Application.IsDedicatedServer ) fireParticles.Update( Renderer, this, Burning );
		if ( Networking.IsHost && Burning && Health > 0f )
		{
			UpdateFire();
			if ( !IsValid || Health <= 0f ) return;
		}
		if ( Networking.IsHost && Movement is CharacterNavigation { Active: true } navigation ) navigation.PlaceOnGround( this );
		if ( Networking.IsHost && (Unconscious || boundAnimations?.Species == "wolf") ) DropHandItem();
		if ( Networking.IsHost && Health > 0 )
		{
			if ( knockoutVoiceDelay > 0f )
			{
				knockoutVoiceDelay -= Time.Delta;
				if ( knockoutVoiceDelay <= 0f && Unconscious ) Speak( "death" );
			}
			blockHealth = Math.Clamp( blockHealth + Time.Delta * 0.3f, 0f, Math.Clamp( TemporaryHealth / MaxHealth, 0f, 1f ) );
			recoverableDamage = Math.Max( 0f, recoverableDamage - MaxHealth * 0.05f * Time.Delta );
		}
		if ( Networking.IsHost && Health > 0 && boundAnimations is not null ) UpdateActiveBlock( Time.Delta );
		// UpdateBrain samples the surroundings while a conscious fighter is busy with an action.
		if ( Networking.IsHost && NpcConVars.Enabled && Health > 0f && Renderer.IsValid() &&
			boundAnimations is not null && !StaticCharacter && !Unconscious )
		{
			UpdateCombatGroup();
			TrackEnemy();
			ScanForCharacters();
		}
		if ( !Downed )
		{
			if ( Networking.IsHost ) UpdatePairedGroundSupport();
			base.OnUpdate();
		}
		if ( Networking.IsHost && Health > 0 && !Downed && InThrow && boundAnimations is not null ) UpdateThrow();
		if ( Networking.IsHost )
		{
			if ( holdPartner is null ) AdvanceCaptureAlignment( ElapsedHoldTicks );
			if ( Holding || Held || holdPartner is not null ) UpdateRearHold();
		}
		if ( restoreAgentPosition && (Downed || Health <= 0f || !(InThrow || Held || Holding)) ) EndPairedMovement();
		if ( Downed || Health < 1 )
		{
			if ( armsActive || earsActive || plantedLegs || chestActive ) ReleaseProceduralPose();
		}
		else if ( (!UsesCombatIK && Stance is not (Threat or Idle or Ready or Watch or Locomotion)) || !HasGroundSupport ) plantedLegs = false;
		if ( Health < 1 || !Renderer.IsValid() || Renderer.Model is null ) return;
		if ( !BindAnimations() ) return;
		if ( Networking.IsHost )
		{
			if ( !Downed )
			{
				UpdatePlantContact();
				CheckIncomingWeapons();
			}
			else plantDepth = 0f;
			UpdateBlockFlinch();
		}
		Renderer.Animation.Parameters.Set( "mirrored_flinch", MirroredFlinch );
		Renderer.Animation.Parameters.Set( "block_flinch", FlinchWeight );
		if ( Networking.IsHost && !Unconscious ) UpdateGazeTarget();
		UpdateMorphs();
		UpdatePickupLayer();
		UpdateWeaponDraw();
		UpdateJumpArms();
		UpdatePainFlinch();
		UpdateKnifeLayers();
		UpdateLanding();
		if ( Downed )
		{
			ReleaseStanceFeet( Time.Delta );
			UpdateBreathing( Time.Delta );
			if ( Networking.IsHost ) TryGetUp();
			else
			{
				if ( recoveryRate > 0f ) EndRecovery();
				CapturePhysicsPose();
			}
			return;
		}
		if ( !HasGroundSupport ) ClearFootContacts();
		if ( !Networking.IsHost && lastPhysicsPose is not null && recoveryRate == 0f )
		{
			if ( RecoveredInAir ) BeginRecovery( 4f );
			else if ( IsGetUp( Stance ) || Stance == Roll ) BeginRecovery( Stance == Roll ? 2f : 1f );
			else lastPhysicsPose = null;
		}
		getUpPoseOffset = GetUpPoseOffset();
		hasSampledPose = false;
		if ( GettingUp && recoveryRate > 0f && (Stance == BlockFromGround || recoveryStep < 0) )
			ReadGetUpPose( Stance == BlockFromGround ? ReactionPhase : 0f );
		if ( Stance == Roll && playing == Roll ) ReadRollPose();
		UpdateRollDisplay();
		UpdatePatrolPose();
		flipLookFade = Math.Clamp( flipLookFade + Time.Delta * (RollProgress < 1f ? 5f : -3f), 0f, 1.1f );
		bool canLook = DragStrength <= 0f && (GazeMovementState || boundAnimations.AttackStates.Contains( Stance ));
		headMotion = null;
		chestMotion = null;
		if ( Networking.IsHost )
		{
			CrouchAmount = duckAmount;
			bool needsCombatPose = NeedsCombatPose();
			UpdateCombatPosture( needsCombatPose );
			UpdateTorsoControl( needsCombatPose );
		}
		UpdateBreathing( Time.Delta );
		var pose = AirPose();
		if ( pose != FreeAir ) jumpCycle.Fade( this, "air_phase" );
		if ( pose != Wall ) wallCycle.Fade( this, "wall_phase" );
		if ( pose == Locomotion && Animation.IsGrounded && GaitSpeed <= 0.6f * 40f )
			pose = CombatPosture || AlertStage == 2 ? Ready : AlertStage == 1 ? Watch : Idle;
		if ( pose == Ready && CombatPosture ) pose = Threat;
		var displayPose = WoundedPose( pose );
		if ( playing is null && poseModel is null )
		{
			// Let the graph produce its first pose before any IK overrides read the bones.
			playing = displayPose;
			Renderer.Animation.Parameters.Set( "stance", playing );
			return;
		}
		if ( playing == PatrolIdle && pose == PatrolIdle && IsPatrolGroup && boundAnimations.PatrolRig is { } patrolRig )
			ReadRetargetedPose( patrolRig, false );
		if ( InThrow && playing == pose && Stance == pose ) ReadThrowPose();
		if ( pose == Wall )
		{
			if ( playing == Wall ) ReadWallPose();
			UpdateWallPose();
		}
		UpdateIdleBalance( pose is Threat or Idle or Ready or Watch );
		if ( pose is ArmedHold or ArmedHeld || pose == PatrolIdle && IsPatrolGroup )
		{
			Renderer.Animation.Parameters.Set( "tall_coord", 1f - Math.Clamp( CrouchAmount, 0f, 1f ) );
			Renderer.Animation.Parameters.Set( "threat_coord", ThreatAmount );
		}
		if ( pose == Locomotion && Networking.IsHost ) MirroredStance = LeftHanded;
		if ( pose != Threat && !InHoldMovementState ) ReleaseStanceFeet( Time.Delta );
		UpdateBalanceSupport();
		if ( pose == Locomotion ) UpdateGait();
		else UpdateBodyLean( 0f );
		Renderer.Animation.Parameters.Set( "close_coord", CloseAmount );
		Renderer.Animation.Parameters.Set( "mirrored_pose", pose is Threat or Idle or Ready or Locomotion ? MirroredStance : MirroredAction );
		if ( LedgeAnimation is not null ) Renderer.Animation.Parameters.Set( "mirrored_pose", LedgeMirrored );
		if ( OnLedge ) UpdateLedgeFeet();
		Array.Clear( actionHandTargets );
		if ( playing == pose && (pose is ClimbUp or LedgeReach || pose == Stance && UsesCombatIK) ) UpdateActionIK( pose );
		if ( IsGrabClip( pose ) )
		{
			var grab = GrabPose( pose );
			grabPhase = playing == pose ? grabPhase + Time.Delta * grab.Frequency : 0f;
			grabPhase = grab.Events.Looping ? grabPhase % 1f : Math.Min( grabPhase, 1f );
			Renderer.Animation.Parameters.Set( "grab_phase", grabPhase );
		}
		if ( OnLedge ) Renderer.Animation.Parameters.Set( "ledge_phase", LedgePhase );
		if ( boundAnimations.IdlePoses.TryGetValue( displayPose, out var idle ) )
		{
			bool entering = playing != displayPose;
			var sample = QueueIdlePose( idle, displayPose != Watch && MirroredStance, entering );
			if ( pose == Threat ) UpdateStanceFeet( sample, entering );
			else if ( Animation.IsGrounded ) UpdateIdleFeet( sample );
		}
		UpdateChestLook( !NoLookAround && RollProgress >= 1f && canLook ? GazeTarget : null );
		if ( headMotion is null ) DrawRestingHead();
		UpdateArms();
		UpdateEars();
		PublishPose();
		UpdateFootSkids( pose );
		ApplyDisplayBreath();
		hasSampledPose = false;
		if ( boundAnimations.AttackStates.Contains( Stance ) )
		{
			Renderer.Animation.Parameters.Set( "attack_height_coord", AttackHeight );
			Renderer.Animation.Parameters.Set( "attack_phase", AttackPhase );
		}
		if ( ReactionPhase < 1f ) Renderer.Animation.Parameters.Set( "reaction_phase", ReactionPhase );
		if ( playing == displayPose ) return;
		playing = displayPose;
		Renderer.Animation.Parameters.Set( "stance", playing );
	}

	// Ground schedules pause in flight, but Overgrowth keeps the active animation and its events running.
	void AdvanceAirborneAction()
	{
		if ( !NpcConVars.Enabled || !IsAirborne || Health <= 0f || !Renderer.IsValid() || boundAnimations is null ||
			Downed || StaticCharacter || Holding || Held || LedgeAnimation is not null ) return;
		AdvanceCombatMomentum( Time.Delta );
		if ( InThrow || AdvanceItemThrow() ) return;
		if ( ReactionPhase < 1f )
		{
			if ( !UpdateDefense() ) Stance = Ready;
			return;
		}
		if ( strike is { AsLayer: false } ) UpdateCombat();
	}

	protected override void Think()
	{
		unassignedPatrol = false;
		if ( !Renderer.IsValid() || boundAnimations is null ) return;
		if ( StaticCharacter )
		{
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			if ( !previousPatrolPoint.IsValid() && PatrolStart.IsValid() && (PatrolStart.WorldPosition - WorldPosition).WithZ( 0 ).LengthSquared < 40f * 40f )
				FollowPatrol();
			return;
		}
		if ( Holding || Held || LedgeAnimation is not null ) return;
		AdvanceCombatMomentum( Time.Delta );
		bool rolling = RollProgress < 1f;
		if ( rolling && AdvanceRoll() ) return;
		UpdateStanceAmounts();
		if ( InThrow || AdvanceItemThrow() || UpdateDefense( !rolling ) ) return;
		if ( RollProgress >= 1f && !GettingUp && strike is not { AsLayer: false } ) hitsOnGround = 0;
		UpdateCombat();
		if ( Stance is Idle or Watch or Ready && Movement is CharacterNavigation navigation && navigation.BrakingVelocity.Length * PreFrictionSpeedScale > 0.6f * 40f )
			Stance = Locomotion;
	}

	bool IsUnaware => AlwaysUnaware || startled > 0f ||
		!enemy.IsValid() && !escortTarget.IsValid() && !soughtItem.IsValid();

	bool NeedsCombatPose()
	{
		if ( startled > 0 ) return false;
		foreach ( var (other, memory) in knownCharacters )
		{
			if ( !memory.Hostile || !memory.Awake || Time.Now - memory.SeenTime >= 1f || !CanAttack( other ) ) continue;
			if ( other.GetComponent<RabbitGuard>() is { IsUnaware: true } ) continue;
			if ( CharacterCenter( GameObject ).DistanceSquared( CharacterCenter( other ) ) < 200f * 200f ) return true;
		}
		return false;
	}

	GameObject enemy;
	Vector3 lastSeen;
	bool visible;
	Vector3? investigatePoint;
	bool urgentInvestigation;

	TimeSince sinceEnemySeen;

	public override string Faction => Factions.Citizen;

	[Property] public string[] Teams { get; set; } = [];
	[Property] public bool AlliedToPlayer { get; set; }

	protected override void SetupRelationships() => SetRelationship( Factions.Player, AlliedToPlayer ? Disposition.Friendly : Disposition.Hostile );

	protected override Relationship GetFallbackRelationship( GameObject other )
	{
		if ( other.GetComponent<CharacterRemains>() is { } remains )
			return new( remains.Teams?.Any( Teams.Contains ) == true ? Disposition.Friendly : Disposition.Hostile );
		var npc = other.GetComponent<BaseNpc>();
		if ( npc is RabbitGuard guard && Teams.Length > 0 && Teams.Any( guard.Teams.Contains ) ) return new( Disposition.Friendly );
		return new( npc.IsValid() ? Disposition.Hostile : Disposition.Neutral );
	}

	static bool IsIgnoredPlayer( GameObject target ) => NpcConVars.NoTarget &&
		(target.Root.Tags.Has( Factions.Player ) || target.GetComponentInParent<PlayerController>().IsValid());

	bool CanAttack( GameObject other ) => IsHostileTarget( other ) && other.GetComponent<RabbitGuard>() is not { Unconscious: true };

	bool IsHostileTarget( GameObject other ) => other.IsValid() && other != GameObject && !IsIgnoredPlayer( other )
		&& !other.GetComponent<CharacterRemains>().IsValid() && IsLiveTarget( other ) && other.GetComponent<BaseNpc>() is not ({ Health: < 1 } or RabbitGuard { StaticCharacter: true })
		&& GetDisposition( other ) == Disposition.Hostile;

	// MovementObject follows the mean physics-bone position while ragdolled. Here the
	// navigation root stays parked until recovery, so observers read the bodies directly.
	Vector3 TargetPosition( GameObject target, bool eyes = false )
	{
		var fighter = target.GetComponent<RabbitGuard>();
		if ( eyes && fighter.IsValid() && CharacterLoader.AverageChainPosition( fighter.Renderer, "head" ) is { } head ) return head;
		var remains = target.GetComponent<CharacterRemains>();
		if ( (fighter is { Downed: true } || remains.IsValid()) &&
			CharacterLoader.RagdollCenter( fighter?.ragdoll ?? remains?.Physics ) is { } center ) return center;
		return eyes ? CalculateEyePosition( target ) : target.WorldPosition;
	}

	Vector3 BodyVelocity => Downed && ragdoll.IsValid() ? CharacterLoader.RagdollVelocity( ragdoll ) ?? Vector3.Zero : Animation.MoveVelocity;

	static Vector3 TargetVelocity( GameObject target )
	{
		if ( target.GetComponent<RabbitGuard>() is { } fighter ) return fighter.BodyVelocity;
		return GetVelocity( target );
	}

	// Standing navigation roots are at the feet. MovementObject.position is at
	// the leg sphere center until the ragdoll takes over.
	internal Vector3 CharacterCenter( GameObject target ) => target.GetComponent<RabbitGuard>() is { Downed: true } || target.GetComponent<CharacterRemains>().IsValid()
		? TargetPosition( target ) : target.WorldPosition + Vector3.Up * (0.45f * 40f);

	void NoticeAttacker( GameObject attacker )
	{
		if ( !StaticCharacter && !CanAttack( enemy ) && CanAttack( attacker ) )
			Remember( attacker, engage: false );
	}

	void Remember( GameObject target, bool engage = true )
	{
		if ( !knownCharacters.ContainsKey( target ) && engage && !enemy.IsValid() && !escortTarget.IsValid() )
		{
			startled = 1f;
			Speak( "engage" );
		}
		if ( !enemy.IsValid() && engage )
		{
			bool frightening = target.GetComponent<RabbitGuard>() is { CausesFearOnSight: true };
			fleeFrom = AlwaysAfraidOnSight || frightening && !NeverAfraidOnSight ? target : null;
			fleeUpdate = 0f;
		}
		ObserveTarget( target, true );
		enemy = target;
		NoticeCharacter( target );
		searchUntil = 0f;
		casualty = null;
		inspectPosition = null;
		escortTarget = null;
		investigatePoint = null;
		urgentInvestigation = false;
		lastSeen = TargetPosition( target );
		sinceEnemySeen = 0;
		visible = true;
		AlertStage = 2;
	}
}
