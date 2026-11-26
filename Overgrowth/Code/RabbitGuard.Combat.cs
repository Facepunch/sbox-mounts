using System;
using Sandbox.Npcs;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string SwordSlashRight = "sword_slash_right";
	RabbitAttack strike;
	GameObject strikeTarget;
	[Property, Range( 0.1f, 2f )] public float AttackSpeed { get; set; } = 1f;
	[Property] public float AttackDamage { get; set; } = 1f;
	[Property] public float AttackKnockback { get; set; } = 1f;
	[Property] public bool LeftHanded { get; set; }
	[Sync] bool MirroredStance { get; set; }
	[Sync] bool MirroredAction { get; set; }
	bool flippedAttack;
	[Sync] float AttackHeight { get; set; }
	[Sync( SyncFlags.Interpolate )] float AttackPhase { get; set; }
	bool impactPassed;
	int nextPrepare;
	RabbitGuard blockingOpponent;
	float predictability;
	readonly AttackHistory attackHistory = new();
	TimeUntil repath;
	float cancelClock;
	bool cancelOnPreviousTick, cancelArmed;
	[Property, Sync] public LevelItem LeftHandItem { get; set; }
	[Property, Sync] public LevelItem RightHandItem { get; set; }
	LevelItem PrimaryItem => LeftHanded ? LeftHandItem : RightHandItem;
	internal bool EquipsForAnimation( LevelItem item ) => PrimaryItem == item && equippedForAnimation == item;
	void DropHandItem()
	{
		reaching = false;
		if ( LeftHandItem.IsValid() )
		{
			seekTime = 0f;
			LeftHandItem.Drop();
			LeftHandItem = null;
		}
		else if ( RightHandItem.IsValid() )
		{
			seekTime = 0f;
			RightHandItem.Drop();
			RightHandItem = null;
		}
	}

	float RangeExtension => PrimaryItem.IsValid() ? PrimaryItem.RangeExtension : 0f;
	float AttackRange => (1.5f + RangeExtension) * (PrimaryItem.IsValid() ? PrimaryItem.RangeMultiplier : 1f) * 40f * CharacterScale;
	float CloseAttackRange => (1f + RangeExtension * 0.5f) * 40f * CharacterScale;
	float StrikeReach( RabbitGuard defender )
	{
		float scale = CharacterScale;
		float extension = 0.1f + RangeExtension;
		if ( defender is { OnLedge: true } && WorldPosition.z > defender.WorldPosition.z ) extension += 0.5f;
		return (1.5f + extension) * 40f * scale;
	}

	bool InStrikeReach( GameObject target, Vector3 offset )
	{
		// An attack already in motion keeps its target through a knockout.
		if ( !IsHostileTarget( target ) ) return false;
		float reach = StrikeReach( target.GetComponent<RabbitGuard>() );
		return offset.LengthSquared < reach * reach;
	}
	bool AnimationCanCancel => Renderer.Animation.Debug.TryGetFloatChannel( "status", "cancel", out var value ) && value >= 1f;

	protected override void OnStart()
	{
		patrolOrigin = WorldTransform;
		base.OnStart();
		if ( PlacedBodyPose is { Count: > 0 } )
		{
			SpawnPlacedBody();
			return;
		}
		if ( Renderer.IsValid() && Renderer.Model is not null ) BindAnimations();
		if ( MovementCapsule.IsValid() ) MovementCapsule.Tags.Add( CharacterCollisionRules.FighterHull );
		// Upright landings and ragdoll contacts have separate Overgrowth damage rules.
		FatalImpactSpeed = 0f;
		initialKnockoutShield = KnockoutShield;
		if ( Networking.IsHost ) MirroredStance = LeftHanded;
	}

	void ResetAttackCancel()
	{
		cancelClock = 0f;
		cancelOnPreviousTick = cancelArmed = false;
	}

	bool CanEndAttack( bool canCancel )
	{
		// UpdateAttackControls reads the last evaluated status at the character tick.
		// Its 10 ms delay requires two open samples at the native four-frame cadence.
		const float step = 4f / 120f;
		cancelClock += Time.Delta;
		if ( cancelClock < step ) return false;
		cancelClock %= step;
		bool end = cancelOnPreviousTick && cancelArmed;
		cancelArmed = cancelOnPreviousTick;
		cancelOnPreviousTick = canCancel;
		return end;
	}

	void UpdateCombat()
	{
		if ( strike is { AsLayer: false } )
		{
			float previousHeight = AttackHeight;
			Vector3 targetOffset = default;
			if ( strikeTarget.IsValid() )
			{
				AttackHeight = GetAttackHeight( strikeTarget );
				targetOffset = CharacterCenter( strikeTarget ) - CharacterCenter( GameObject );
				TurnDuringStrike( strikeTarget.GetComponent<RabbitGuard>() is { IsDodging: true } ? WorldRotation.Forward : targetOffset );
			}
			bool blocked = Stance == strike.Blocked;
			var blockClip = blocked ? boundAnimations.Clips[Stance].ForSide( MirroredAction ) : null;
			var blend = blocked ? new RabbitAttack.Blend( blockClip, blockClip, 0f ) : strike.Sample( AttackHeight, MirroredAction );
			var clip = blend.Events;
			if ( strikeTarget.IsValid() )
			{
				bool beforeImpact = !blocked && clip.Impact is { } hit && AttackPhase < hit.Time / clip.Duration;
				AdjustStrikeSpacing( targetOffset, strikeTarget.GetComponent<RabbitGuard>() is { Downed: true }, beforeImpact );
			}
			var previous = !blocked && previousHeight != AttackHeight ? strike.Sample( previousHeight, MirroredAction ) : blend;
			var from = previous.Motion( AttackPhase );
			AttackPhase = Math.Min( 1f, AttackPhase + Time.Delta * AttackSpeed * blend.Frequency );
			ApplyRootMotion( from, blend.Motion( AttackPhase ) );
			while ( !blocked && nextPrepare < clip.PreparePhases.Length && AttackPhase >= clip.PreparePhases[nextPrepare] )
			{
				nextPrepare++;
				if ( strikeTarget.IsValid() && InStrikeReach( strikeTarget, CharacterCenter( strikeTarget ) - CharacterCenter( GameObject ) ) &&
					strikeTarget.GetComponent<RabbitGuard>() is { } defender && defender.TryActiveBlock( this, strike ) )
				{
					dontAttackBefore = Random.Shared.Float( 0f, 1.4f );
					blockingOpponent = defender;
					Stance = strike.Blocked;
					clip = boundAnimations.Clips[Stance].ForSide( MirroredAction );
					// SwapAnimation preserves elapsed time. A synced group uses its first clip as the clock.
					AttackPhase = Math.Min( 1f, AttackPhase * strike.Poses[0].Clip.Duration / clip.Duration );
					if ( strike.SwapStance != strike.SwapBlocked ) MirroredStance = !MirroredStance;
					blocked = true;
				}
			}
			if ( !impactPassed && (blocked ? clip.Blocked : clip.Impact) is { } impact && AttackPhase >= impact.Time / clip.Duration )
			{
				impactPassed = true;
				if ( !blocked ) TryHit( clip, impact.Bone );
				else if ( blockingOpponent.IsValid() )
				{
					var pos = Renderer.TryGetBoneTransform( $"bone_{impact.Bone}", out var bone ) ? bone.Position : EyePosition;
					blockingOpponent.InterruptWeaponLayers();
					stunnedBy = blockingOpponent;
					blockStun = 0.5f;
					var species = blockingOpponent.boundAnimations.Species;
					bool smaller = boundAnimations.Species == "dog" && species is not ("dog" or "wolf") || species == "rat" && boundAnimations.Species != "rat";
					blockingOpponent.ReceiveBlockImpact( this, strike, smaller ? 4f : 1f );
					if ( !strike.FleshUnblockable ) PlayBluntImpact( "hit_block", pos );
					else blockingOpponent.ClashWith( PrimaryItem );
					blockingOpponent = null;
				}
			}
			if ( AttackPhase < 1f && !CanEndAttack( AnimationCanCancel ) ) return;
			strike = null;
			EndGroupAttack();
			Stance = Threat;
			return;
		}

		// UpdateGroundAttackControls only runs on the ground. The rabbit's airborne
		// leg-cannon controls are player-only; an AI finishes its crossing first.
		if ( !Animation.IsGrounded )
		{
			Stance = Locomotion;
			return;
		}

		if ( Flee() || (soughtItem is not null && SeekWeapon()) ) return;
		if ( enemy is not null && ClosestKnownThreat() is { } closest && closest != enemy )
		{
			Remember( closest, startle: false );
			visible = CanSeeCharacter( closest, closest.GetComponent<RabbitGuard>() );
		}
		if ( !IsKnownThreat( enemy ) )
		{
			if ( enemy is not null && !enemy.IsValid() ) FinishCombat( lastSeen );
			else if ( enemy.IsValid() && knownCharacters.TryGetValue( enemy, out var memory ) && !memory.Awake )
				FinishCombat( memory.Position );
			enemy = null;
			if ( FollowHeardVoice() ) return;
			if ( InvestigateCasualty() ) return;
			if ( !investigatePoint.HasValue && SearchArea() ) return;
			if ( investigatePoint.HasValue && startled > 0 )
			{
				ReleaseMovementInput();
				Stance = Ready;
				return;
			}
			if ( investigatePoint is { } point )
			{
				if ( ApproachSite( point, urgentInvestigation, urgentInvestigation ? 0.7f : 0.2f ) is not { } reached ) return;
				investigatePoint = null;
				if ( urgentInvestigation )
				{
					urgentInvestigation = false;
					InspectSite( reached );
					return;
				}
			}
			if ( FollowPatrol() ) return;
			ReleaseMovementInput();
			Stance = AlertStage > 0 ? Watch : Idle;
			return;
		}

		if ( startled > 0 )
		{
			ReleaseMovementInput();
			Stance = Ready;
			return;
		}
		if ( FindWeapon() || TryCounterThrow() ) return;
		var destination = visible ? TargetPosition( enemy ) : lastSeen;
		var delta = visible ? CharacterCenter( enemy ) - CharacterCenter( GameObject ) : destination - WorldPosition;
		float distance = delta.Length;
		if ( !Omniscient && !visible && sinceEnemySeen > 2f )
		{
			var lastKnown = observedTarget == enemy && observationCount > 0 ? RecallTarget( Time.Now ).Position : destination;
			enemy = null;
			BeginAreaSearch( lastKnown );
			SearchArea();
			return;
		}

		if ( visible ) UpdateTactic( distance );
		if ( Stance == Roll ) return;
		if ( TryStartStrike() ) return;

		float speed = Animation.MoveVelocity.WithZ( 0f ).Length;
		var footStanceTarget = CombatFootStanceTarget( speed, NeedsCombatPose() );
		if ( repath <= 0 )
		{
			repath = 0.2f;
			var intent = ChaseApproach( destination, out var point );
			MoveWithIntent( point, intent, 1f, footStanceTarget );
		}
		Stance = !Movement.MoveTarget.HasValue || footStanceTarget.IsValid() || speed <= 0.6f * 40f ? Threat : Locomotion;
	}

	bool InAttackState => !Downed && (strike is { AsLayer: false } || Stance == "throw" || IsThrowingItem || Holding && !CaptureReactionEnded || FadingGrab);

	GameObject CurrentAttackTarget => Stance == "throw" ? throwPartner?.GameObject
		: IsThrowingItem ? itemThrowTarget
		: Holding ? holdPartner?.GameObject
		: FadingGrab ? ReleasedGrabTarget
		: strikeTarget;

	// GetAttackTarget first considers attackers, then standing characters, then conscious ragdolls.
	// The chosen target remains bound to this strike while the AI continues tracking its chase target.
	GameObject FindStrikeTarget( out float distance )
	{
		var center = CharacterCenter( GameObject );
		float range = AttackRange;
		float ledgeRange = range + 0.5f * 40f;
		float nearest = float.PositiveInfinity, nearestLedge = float.PositiveInfinity;
		GameObject ledge = null;
		int bestPriority = int.MaxValue;
		GameObject best = null;
		void Consider( GameObject candidate )
		{
			if ( candidate == best || !CanAttack( candidate ) ) return;
			var guard = candidate.GetComponent<RabbitGuard>();
			int priority = guard is { InAttackState: true } ? 0 : guard is { Downed: true } ? 2 : 1;
			float separation = center.DistanceSquared( CharacterCenter( candidate ) );
			if ( separation > range * range )
			{
				if ( guard is { OnLedge: true } && separation <= ledgeRange * ledgeRange && separation < nearestLedge )
				{
					ledge = candidate;
					nearestLedge = separation;
				}
				return;
			}
			if ( priority > bestPriority || (priority == bestPriority && separation >= nearest) ) return;
			best = candidate;
			nearest = separation;
			bestPriority = priority;
		}
		Consider( enemy );
		foreach ( var hit in Scene.FindInPhysics( new Sphere( center, ledgeRange ) ) )
		{
			var candidate = hit.GetComponentInParent<BaseNpc>()?.GameObject ?? hit.GetComponentInParent<PlayerController>()?.GameObject;
			if ( candidate.IsValid() ) Consider( candidate );
		}
		if ( best is null && ledge.IsValid() && center.z > CharacterCenter( ledge ).z )
		{
			best = ledge;
			nearest = nearestLedge;
		}
		distance = MathF.Sqrt( nearest );
		return best;
	}

	bool WantsToAttack => CombatAllowed && startled <= 0f && !(boundAnimations.Species == "wolf" && blockStun > 0.5f)
		&& (airResponse is AirResponse.Counter or AirResponse.KnockOffLedge || airResponse != AirResponse.Evade && tactic is CombatTactic.Rush or CombatTactic.Wait);

	bool TryStartStrike()
	{
		if ( !WantsToAttack || strike is not null || knifeCooldown > 0 ) return false;

		var target = FindStrikeTarget( out float distance );
		if ( !target.IsValid() ) return false;
		// GetAttackDirection falls back to the unit facing vector when the opponent dodges.
		if ( target.GetComponent<RabbitGuard>() is { IsDodging: true } ) distance = 40f;
		bool close = distance < CloseAttackRange;
		int attackClass = airResponse switch
		{
			AirResponse.KnockOffLedge => 0,
			AirResponse.Counter => 1,
			_ => Random.Shared.Next( 3 )
		};
		bool mirrored = Random.Shared.Next( 2 ) == 0;
		var label = boundAnimations.WeaponLabel;
		if ( label == "sword" && attackClass == 0 ) attackClass = 1;
		var choice = attackClass switch
		{
			1 or 2 when (label is "sword" or "rapier") && mirrored == LeftHanded => SwordSlashRight,
			1 when !PrimaryItem.IsValid() && target.GetComponent<RabbitGuard>() is { Downed: true } => SoccerKick,
			1 when PrimaryItem.IsValid() || !IsCrouching( target ) => close ? Jab : Roundhouse,
			2 => Sweep,
			_ => close ? Knee : Kick
		};
		strike = boundAnimations.Attacks[choice];
		punishGroundedTarget = null;
		// The original adds exertion on attack selection and again on entering the full-body attack state.
		IncreaseBreathing( strike.AsLayer ? 2f : 4f );
		if ( strike.AsLayer )
		{
			BeginKnifeAttack( target );
			return true;
		}
		strikeTarget = target;
		StopForCombat();
		Stance = choice;
		if ( strike.Direction != "front" )
			MirroredStance = mirrored != (strike.Direction == "left");
		MirroredAction = MirroredStance;
		flippedAttack = strike.Direction == "front" ? !MirroredStance : MirroredStance;
		if ( strike.SwapStance ) MirroredStance = !MirroredStance;
		duckAmount = strike.Height == "low" ? 1f : 0f;
		PlayAttackStart( strike.MaterialEvent );
		AttackPhase = 0;
		ResetAttackCancel();
		AttackHeight = GetAttackHeight( target );
		impactPassed = false;
		nextPrepare = 0;
		predictability = attackHistory.Record( strike, flippedAttack );
		return true;
	}

	// GetNavMeshMovement slows only the component along the line to the target.
	Vector3 CombatApproach( Vector3 target, out Vector3 point ) =>
		MovementToPoint( target, Math.Max( 8f, 40f - preferredRange ), preferredRange, strafeOffset, out point );

	void AdjustStrikeSpacing( Vector3 offset, bool ragdolled, bool beforeImpact )
	{
		float distance = offset.Length;
		if ( distance <= 0f ) return;
		float correction, strength;
		if ( ragdolled && distance < LegRadius * 2f )
		{
			correction = distance - LegRadius * 2f;
			strength = 0.1f;
		}
		else if ( beforeImpact && distance > LegRadius * 3f && distance < LegRadius * 3f + 40f && GameDifficulty < 0.5f )
		{
			correction = distance - LegRadius * 3f;
			strength = 0.1f * (1f - GameDifficulty * 2f);
		}
		else return;
		// An AI script tick moves ten percent of the remaining distance at 30 Hz.
		float blend = 1f - MathF.Pow( 1f - strength, Time.Delta * 30f );
		MoveGroundDelta( offset * (correction * blend / distance) );
	}

	void TurnDuringStrike( Vector3 direction )
	{
		var target = direction.WithZ( 0f ).Normal;
		if ( target.IsNearlyZero() ) return;
		bool armed = PrimaryItem.IsValid();
		if ( armed && boundAnimations.WeaponLabel == "spear" ) target = Rotation.FromYaw( 0.05f.RadianToDegree() ) * target;
		float blend = 1f - MathF.Pow( armed ? 0.8f : 0.9f, Time.Delta * 120f );
		WorldRotation = InterpolateFacing( WorldRotation.Forward, target, blend );
	}

	float GetAttackHeight( GameObject target )
	{
		// Overgrowth targets the mean position of the rig's physics bones.
		var model = target.GetComponent<BaseNpc>()?.Renderer;
		var center = CharacterLoader.AverageBodyPosition( model );
		if ( center is { } average ) return (average.z - WorldPosition.z) / 40f - 0.9f;
		float sum = 0;
		int count = 0;
		if ( model.IsValid() && model.Model?.Physics is { } physics )
		{
			foreach ( var part in physics.Parts )
			{
				if ( !model.TryGetBoneTransform( part.BoneName, out var bone ) ) continue;
				sum += bone.Position.z;
				count++;
			}
		}

		var targetZ = count > 0 ? sum / count : target.GetComponent<PlayerController>() is { } player
			? player.WorldPosition.z + player.CurrentHeight * 0.5f : target.GetBounds().Center.z;
		// The movement object's origin is one leg-sphere radius above our ground origin.
		return (targetZ - WorldPosition.z) / 40f - (0.45f + 0.45f);
	}

	static bool IsCrouching( GameObject target ) => target.GetComponent<RabbitGuard>() is { duckAmount: >= 0.5f }
		|| target.GetComponent<PlayerController>() is { IsDucking: true };

	static bool TargetIsAirborne( GameObject target ) => target.GetComponent<BaseNpc>() switch
	{
		RabbitGuard { InThrow: true } fighter => !fighter.HasGroundSupport,
		{ } npc => npc.IsAirborne,
		_ => target.GetComponent<PlayerController>() is { IsOnGround: false }
	};

	static bool AvoidsStrike( GameObject target, string height ) => target.GetComponent<RabbitGuard>() is { IsDodging: true } || height switch
	{
		"high" => IsCrouching( target ),
		"low" => TargetIsAirborne( target ),
		_ => false
	};

	void TryHit( RabbitClip clip, int bone )
	{
		if ( !strikeTarget.IsValid() ) return;
		var position = Renderer.TryGetBoneTransform( $"bone_{bone}", out var contact ) ? contact.Position : EyePosition;
		var origin = CharacterCenter( GameObject );
		var offset = CharacterCenter( strikeTarget ) - origin;
		bool reached = InStrikeReach( strikeTarget, offset );
		var rabbit = reached ? strikeTarget.GetComponent<RabbitGuard>() : null;
		bool wasUnaware = rabbit?.IsUnaware == true;
		if ( rabbit is not null )
		{
			rabbit.InterruptWeaponLayers();
			if ( rabbit.boundAnimations?.Species == "wolf" && !rabbit.knownCharacters.ContainsKey( GameObject ) ) rabbit.blockStun = 1f;
			rabbit.NoticeAttacker( GameObject );
			if ( rabbit.Downed || rabbit.RollProgress < 1f || rabbit.GettingUp ) rabbit.hitsOnGround++;
		}
		if ( !reached || AvoidsStrike( strikeTarget, strike.Height ) )
		{
			PlayCombatSound( "Data/Sounds/whoosh/hit_whoosh.xml", position );
			return;
		}
		rabbit?.ReleaseTetherExcept( this );
		float sharp = Random.Shared.Float( strike.SharpMin, strike.SharpMax );
		if ( rabbit.IsValid() && boundAnimations.Species == "wolf" ) rabbit.Stab( position, offset.Normal, true );
		bool parried = rabbit is not null && rabbit.TryPassiveBlock( this, strike, wasUnaware, sharp );
		if ( !parried && wasUnaware && rabbit.boundAnimations?.Species == "dog" && boundAnimations.WeaponLabel == "sword" )
			rabbit.ReceiveSurpriseSwordHit();
		var direction = offset.Normal;
		if ( sharp > 0f )
		{
			ApplySharpHit( clip, position, direction, rabbit, parried, sharp );
			return;
		}
		bool armored = rabbit is { MetalArmor: true };
		var group = "hit_normal";
		if ( !parried )
		{
			var npc = strikeTarget.GetComponent<BaseNpc>();
			ApplyStrikeImpulse( clip, position, direction );
			if ( npc is not null && (!npc.IsValid() || npc.Health < 1f) ) group = "hit_hard";
			else if ( npc is RabbitGuard { Downed: true } ) group = "hit_medium";
			if ( rabbit.IsValid() && strike is not null ) rabbit.TryDisarm( strike.WorldImpact( direction, flippedAttack ) );
		}
		PlayBluntImpact( group, position );
		if ( armored && Random.Shared.Next( 3 ) != 0 )
			PlayCombatSound( "Data/Sounds/weapon_foley/impact/weapon_staff_hit_metal_strong.xml", position );
		if ( strikeTarget.IsValid() ) ReportSound( strikeTarget, position, HeardSound.Combat, 10f );
	}

	(Vector3 Impulse, Vector3 Line) StrikeContact( Vector3 direction )
	{
		var impulse = strike.WorldImpact( direction, flippedAttack ) * (strike.Force * AttackKnockback * 40f / 120f);
		return (impulse, PrimaryItem.IsValid() ? PrimaryItem.StrikeLineDirection() : default);
	}

	void ApplyStrikeImpulse( RabbitClip clip, Vector3 position, Vector3 direction, float sharp = 0f )
	{
		var victim = strikeTarget.GetComponentInParent<IDamageable>();
		if ( victim is null ) return;
		var origin = CharacterCenter( GameObject );
		var (impulse, line) = StrikeContact( direction );
		if ( strikeTarget.GetComponent<RabbitGuard>() is { MaxHealth: > 0 } defender )
			impulse *= defender.StrikeForceScale;
		var damage = new StrikeDamage
		{
			SharpDamage = sharp,
			Impulse = impulse,
			LineDirection = line,
			Damage = Random.Shared.Float( clip.DamageMin, clip.DamageMax ) * 100f * AttackDamage,
			Origin = origin,
			Position = position,
			Attacker = GameObject,
			Weapon = PrimaryItem.IsValid() ? PrimaryItem.GameObject : GameObject
		};
		damage.Tags.Add( "melee" );
		damage.Tags.Add( "nevergib" );
		victim.OnDamage( damage );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayBluntImpact( string group, Vector3 position )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		mount.PlayGroup( $"Data/Sounds/hit/{group}.xml", position );
		mount.EmitImpactDust( "Data/Particles/impactfast.xml", position );
		mount.EmitImpactDust( "Data/Particles/impactslow.xml", position );
	}

	void ClashWith( LevelItem other )
	{
		if ( other.IsValid() && PrimaryItem.IsValid() && PrimaryItem.TryContact( other, out var point, out var a, out var b ) )
			PlayWeaponClash( point, a, b );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayWeaponClash( Vector3 point, string a, string b )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		var group = (a, b) switch
		{
			("metal", "metal") => "weapon_metal_hit_metal_strong",
			("wood", "wood") => "weapon_staff_hit_staff_strong",
			_ => "weapon_staff_hit_metal_strong"
		};
		mount.PlayGroup( $"Data/Sounds/weapon_foley/impact/{group}.xml", point );
		if ( a == "metal" && b == "metal" )
		{
			mount.MetalSparks( point, SecondaryPoseFrames );
			return;
		}
		mount.EmitClashParticles( "woodspeck", point, Random.Shared.Next( a == "wood" && b == "wood" ? 5 : 10 ) );
		mount.EmitImpactDust( "Data/Particles/impactfast.xml", point );
		mount.EmitImpactDust( "Data/Particles/impactslow.xml", point );
	}

	void PlayAttackStart( string materialEvent )
	{
		Speak( "attack" );
		if ( !string.IsNullOrEmpty( materialEvent ) )
			PlayMaterialEvent( materialEvent, WorldPosition, GroundMaterialName, 1f );
	}

	void Speak( string cue )
	{
		if ( cue is "attack" or "engage" ) ReportBodySound( HeardSound.Voice, 10f );
		CharacterVoice.Select( Renderer, cue, PlayVoice );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayVoice( string path, float volume, float pitch, float distance )
		=> PlayLocalVoice( path, volume, pitch, distance );

	internal void PlayLocalVoice( string path, float volume, float pitch, float distance )
	{
		if ( Renderer.IsValid() ) voice.Play( Renderer, TargetPosition( GameObject, eyes: true ), path, volume, pitch, distance );
	}

	internal void StopLocalVoice()
	{
		knockoutVoiceDelay = 0f;
		voice.Stop();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayCombatSound( string group, Vector3 position, float gain = 1f )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
			mount.PlayGroup( group, position, gain );
	}
}
