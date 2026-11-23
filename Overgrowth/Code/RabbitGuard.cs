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
	[Property] public bool StaticCharacter { get; set; }
	[Property] public bool AlwaysUnaware { get; set; }
	TimeUntil nextVisionCheck = 1f;
	float enemySeen;
	string playing;
	[Sync] string Stance { get; set; } = Idle;
	internal bool InLocomotion => Stance == Locomotion;
	RabbitGuardAnimations boundAnimations;
	LevelItem equippedForAnimation;
	string boundStanceOverride;

	void IHotloadManaged.Created( IReadOnlyDictionary<string, object> state )
	{
		boundAnimations = null;
		if ( MovementCapsule.IsValid() ) MovementCapsule.Tags.Add( LevelItem.FighterHull );
		strike = null;
	}

	bool BindAnimations()
	{
		var equipped = PrimaryItem.IsValid() ? PrimaryItem : null;
		if ( boundAnimations is null || Renderer.Animation.Graph is null || boundStanceOverride != UnarmedStanceOverride || equippedForAnimation != equipped && !IsThrowingItem )
		{
			if ( CharacterLoader.FromModel( Renderer.Model ) is not { } loader || loader.GetGuardAnimations( equipped?.GetComponent<ModelRenderer>()?.Model, UnarmedStanceOverride ) is not { } ready ) return false;
			if ( !Downed ) Renderer.ClearPhysicsBones();
			equippedForAnimation = equipped;
			boundStanceOverride = UnarmedStanceOverride;
			boundAnimations = ready;
			strike = null;
			if ( Networking.IsHost && GetComponent<Rigidbody>() is { } body ) body.GravityScale = FallGravityScale( Scene.PhysicsWorld );
			// Overgrowth reseeds secondary motion for four animation updates after a discontinuity.
			secondaryReset = 4f * (4f / 120f);
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
			base.OnUpdate();
			if ( Networking.IsHost && NpcConVars.Enabled && IsAirborne && Health > 0f && Renderer.IsValid() &&
				boundAnimations is not null && !Downed && !StaticCharacter && !Holding && !Held && LedgeAnimation is null )
				AdvanceAirborneAction();
		}
		if ( Networking.IsHost && Health > 0 && !Downed && InThrow && boundAnimations is not null ) UpdateThrow();
		if ( Networking.IsHost && (Holding || Held || holdPartner is not null) ) UpdateRearHold();
		if ( restoreAgentPosition && (Downed || Health <= 0f || !(InThrow || Held || Holding)) ) EndPairedMovement();
		if ( Downed || Health < 1 )
		{
			if ( armsActive || earsActive || plantedLegs || chestActive ) ReleaseProceduralPose();
		}
		else if ( (!InThrow && Stance is not (Threat or Idle or Ready or Watch or Locomotion)) || !HasGroundSupport ) plantedLegs = false;
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
		if ( Networking.IsHost ) UpdateGazeTarget();
		hasGaitPose = false;
		if ( Stance == Roll && playing == Roll ) ReadRollPose();
		UpdateRollDisplay();
		UpdatePatrolPose();
		flipLookFade = Math.Clamp( flipLookFade + Time.Delta * (RollProgress < 1f ? 5f : -3f), 0f, 1.1f );
		bool canLook = DragStrength <= 0f && (GazeMovementState || boundAnimations.AttackStates.Contains( Stance ));
		headMotion = chestMotion = null;
		if ( Networking.IsHost )
		{
			bool needsCombatPose = NeedsCombatPose();
			UpdateCombatPosture( needsCombatPose );
			UpdateTorsoControl( needsCombatPose );
		}
		UpdateBreathing( Time.Delta );
		var pose = AirPose();
		if ( pose == Locomotion && Animation.IsGrounded && Animation.MoveVelocity.WithZ( 0f ).Length <= 0.6f * 40f )
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
		if ( pose == Locomotion && playing == Locomotion ) ReadGaitPose();
		Renderer.Animation.Parameters.Set( "close_coord", CloseAmount );
		if ( pose == Wall )
		{
			if ( playing == Wall ) ReadWallPose();
			UpdateWallPose();
		}
		UpdateIdleBalance( pose is Threat or Idle or Ready or Watch );
		if ( pose is ArmedHold or ArmedHeld )
		{
			Renderer.Animation.Parameters.Set( "tall_coord", 1f - Math.Clamp( CrouchAmount, 0f, 1f ) );
			Renderer.Animation.Parameters.Set( "threat_coord", ThreatAmount );
		}
		if ( pose == Locomotion && Networking.IsHost ) MirroredStance = LeftHanded;
		Renderer.Animation.Parameters.Set( "mirrored_pose", pose is Threat or Idle or Ready or Locomotion ? MirroredStance : MirroredAction );
		if ( LedgeAnimation is not null ) Renderer.Animation.Parameters.Set( "mirrored_pose", LedgeMirrored );
		if ( pose != Threat ) ReleaseStanceFeet( Time.Delta );
		if ( pose == Locomotion ) UpdateGait();
		else UpdateBodyLean( 0f );
		if ( OnLedge ) UpdateLedgeFeet();
		if ( InThrow && playing == Stance && HasGroundSupport ) UpdateThrowFeet();
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
		hasGaitPose = false;
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
		if ( AdvanceRoll() ) return;
		UpdateStanceAmounts();
		if ( InThrow || AdvanceItemThrow() || UpdateDefense() ) return;
		if ( RollProgress >= 1f && !IsGetUp( Stance ) ) hitsOnGround = 0;
		UpdateCombat();
		if ( Stance is Idle or Watch or Ready && Movement is CharacterNavigation navigation && navigation.BrakingVelocity.Length > 0.6f * 40f )
			Stance = Locomotion;
	}

	// NeedsCombatPose considers every hostile seen in the last second, including those outside the current goal.
	readonly Dictionary<GameObject, float> seenHostiles = new();

	bool IsUnaware => AlwaysUnaware || startled > 0f ||
		!enemy.IsValid() && !escortTarget.IsValid() && !soughtItem.IsValid();

	bool NeedsCombatPose()
	{
		if ( startled > 0 ) return false;
		foreach ( var (other, seen) in seenHostiles )
		{
			if ( Time.Now - seen >= 1f || !CanAttack( other ) ) continue;
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
	TimeUntil hearingReady = 1f;

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

	bool CanAttack( GameObject other ) => other.IsValid() && other != GameObject && !IsIgnoredPlayer( other )
		&& !other.GetComponent<CharacterRemains>().IsValid() && IsLiveTarget( other ) && other.GetComponent<BaseNpc>() is not ({ Health: < 1 } or RabbitGuard { Unconscious: true } or RabbitGuard { StaticCharacter: true })
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

	// Standing navigation roots are at the feet. MovementObject.position is at
	// the leg sphere center until the ragdoll takes over.
	Vector3 CharacterCenter( GameObject target ) => target.GetComponent<RabbitGuard>() is { Downed: true } || target.GetComponent<CharacterRemains>().IsValid()
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
		seenHostiles[target] = Time.Now;
		escortTarget = null;
		investigatePoint = null;
		urgentInvestigation = false;
		lastSeen = TargetPosition( target );
		sinceEnemySeen = 0;
		visible = true;
		AlertStage = 2;
	}
}
