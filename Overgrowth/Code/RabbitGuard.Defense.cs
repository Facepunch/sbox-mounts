using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property, Range( 0f, 1f )] public float BlockSkill { get; set; } = 0.5f;
	TimeUntil blockRecharge, activeBlock, blockFlinchRaised;
	float? blockDelay;
	float flinchSpring, flinchVelocity, flinchFade, flinchFadeOut, flinchOpacity;
	float flinchRemainder, displayedFlinch;
	bool flinching;
	[Sync] bool MirroredFlinch { get; set; }
	[Sync( SyncFlags.Interpolate )] float FlinchWeight { get; set; }

	TimeUntil startled;
	float blockHealth = 1f;
	int hitsOnGround;
	float duckAmount, duckVelocity, groundCrouchTarget;
	double groundCrouchTime;
	float reactionDuration;
	bool activeBlockReaction;
	[Sync( SyncFlags.Interpolate )] float ReactionPhase { get; set; } = 1f;

	void UpdateBlockFlinch()
	{
		float dt = Time.Delta;
		// ActiveBlocking lasts .2 seconds; the spring stays raised for .3 from activation.
		bool raised = blockFlinchRaised > 0f;
		float target = raised ? 1f : plantDepth * Math.Min( 1f, Animation.MoveVelocity.Length / 40f );
		const float tick = 4f / 120f;
		float stiffness = raised ? 300f : 150f;
		float damping = MathF.Pow( raised ? 0.8f : 0.86f, 4f );
		flinchRemainder += dt;
		while ( flinchRemainder >= tick )
		{
			flinchVelocity = Math.Clamp( flinchVelocity + (target - flinchSpring) * tick * stiffness, -1e16f, 1e16f );
			if ( MathF.Abs( flinchVelocity ) < 1e-14f ) { flinchVelocity = 0f; flinchSpring = target; }
			flinchSpring += flinchVelocity * tick;
			flinchVelocity *= damping;
			flinchRemainder -= tick;
		}
		displayedFlinch = flinchSpring + (flinchVelocity + (target - flinchSpring) * tick * stiffness) * flinchRemainder;
		bool settled = MathF.Abs( flinchSpring ) < 0.01f && MathF.Abs( flinchVelocity ) < 0.01f;
		if ( flinching && settled )
		{
			flinching = false;
			flinchFadeOut = 4f;
		}
		if ( !flinching && !settled && !draggedBody.IsValid() && ReactionPhase >= 1f && !InThrow && !Downed && NeedsCombatPose() )
		{
			flinching = true;
			MirroredFlinch = MirroredStance;
		}
		if ( flinching ) flinchOpacity = Math.Clamp( displayedFlinch, 0.00001f, 1f );
		flinchFade = Math.Clamp( flinchFade + dt * (flinching ? 10f : -flinchFadeOut), 0f, 1f );
		FlinchWeight = flinchFade * flinchOpacity;
	}

	bool InspectingBody => inspectUntil.HasValue && !enemy.IsValid() && (casualty.IsValid() || inspectPosition.HasValue);

	float GroundCrouchTarget()
	{
		float speed = Animation.MoveVelocity.WithZ( 0f ).Length / 40f;
		float target = InspectingBody ? 1f : 0f;
		if ( stanceMove ) target *= 1f - 0.7f * Math.Min( 1f, speed / 5f );
		if ( draggedBody.IsValid() && DragStrength < 0.7f ) target = 1f;
		if ( currentKnifeLayer is { Active: true } && strikeTarget.IsValid() )
			target = Math.Max( target, Math.Clamp( -GetAttackHeight( strikeTarget ), 0f, 1f ) );
		return target;
	}

	bool UpdateDefense( bool advanceCrouch = true )
	{
		if ( advanceCrouch && strike is not { AsLayer: false } && (ReactionPhase >= 1f || IsGetUp( Stance )) )
		{
			// UpdateDuckAmount integrates the target from the preceding movement update.
			AdvanceCrouchSpring( groundCrouchTarget, Time.Delta );
			groundCrouchTime = Time.NowDouble;
			if ( !IsGetUp( Stance ) ) groundCrouchTarget = GroundCrouchTarget();
		}
		if ( ReactionPhase >= 1f || reactionDuration <= 0f ) return false;
		float elapsed = ReactionPhase * reactionDuration;
		if ( activeBlockReaction && elapsed > 0.1f && Animation.IsGrounded && startled <= 0f )
		{
			if ( TryCounterThrow() ) return true;
			if ( CanAttack( enemy ) && TryStartStrike() )
			{
				activeBlockReaction = false;
				ReactionPhase = 1f;
				return true;
			}
		}
		if ( !IsGetUp( Stance ) && (elapsed > (activeBlockReaction ? 0.2f : 0.6f) || elapsed > 0.1f && AnimationCanCancel) )
		{
			ReactionPhase = 1f;
			return false;
		}
		if ( Time.Delta <= 0f ) return true;
		var clip = boundAnimations.Clips[Stance].ForSide( MirroredAction ).Animation;
		var from = clip.GetRootMotion( ReactionPhase );
		if ( ReactionPhase == 0f )
		{
			// A fresh animation reader starts at identity. Consume its first 120 Hz
			// sample before applying the rest of the displacement in the new facing.
			from = clip.GetRootMotion( Math.Min( 1f, Math.Min( Time.Delta, 1f / 120f ) / reactionDuration ) );
			ApplyRootMotion( global::Transform.Zero, from );
		}
		ReactionPhase = Math.Min( 1f, ReactionPhase + Time.Delta / reactionDuration );
		ApplyRootMotion( from, clip.GetRootMotion( ReactionPhase ) );
		if ( IsGetUp( Stance ) && ReactionPhase >= 1f )
		{
			duckAmount = CrouchAmount = groundCrouchTarget = 1f;
			duckVelocity = 0f;
		}
		return ReactionPhase < 1f;
	}

	// UpdateDuckAmount updates velocity before position once per four game ticks.
	// Taking a fractional power of that update preserves both the pose and momentum between ticks.
	const float CrouchStep = 4f / 120f;
	const float CrouchDamping = 0.89f * 0.89f * 0.89f * 0.89f;
	const float CrouchImpulse = 120f * CrouchStep * CrouchDamping;
	const float CrouchTraceHalf = (1f - CrouchStep * CrouchImpulse + CrouchDamping) * 0.5f;
	static readonly float CrouchSine = MathF.Sqrt( CrouchDamping - CrouchTraceHalf * CrouchTraceHalf );
	static readonly float CrouchAngle = MathF.Atan2( CrouchSine, CrouchTraceHalf );

	void AdvanceCrouchSpring( float target, float delta )
		=> (duckAmount, duckVelocity) = SampleCrouchSpring( target, delta );

	(float Amount, float Velocity) SampleCrouchSpring( float target, float delta )
	{
		float offset = duckAmount - target;
		if ( offset == 0f && duckVelocity == 0f ) return (duckAmount, duckVelocity);
		float nextVelocity = CrouchDamping * duckVelocity - CrouchImpulse * offset;
		float nextOffset = offset + CrouchStep * nextVelocity;
		float steps = delta / CrouchStep;
		float decay = MathF.Pow( CrouchDamping, steps * 0.5f );
		float next = decay * MathF.Sin( steps * CrouchAngle ) / CrouchSine;
		float current = decay * MathF.Cos( steps * CrouchAngle ) - next * CrouchTraceHalf;
		return (target + current * offset + next * nextOffset, current * duckVelocity + next * nextVelocity);
	}

	void UpdateActiveBlock( float step )
	{
		if ( IsTethered )
		{
			if ( activeBlock > 0f ) blockRecharge = 0.2f;
			else if ( blockRecharge > 0f ) blockRecharge += step;
			activeBlock = 0f;
			return;
		}
		if ( boundAnimations.Species == "wolf" ) return;
		var opponent = startled <= 0f && HasCombatTarget ? enemy.GetComponent<RabbitGuard>() : null;
		var move = opponent?.strike;
		if ( move is not null && opponent.Stance == move.Blocked ) move = null;
		if ( move is { AsLayer: false } && blockDelay is null && blockRecharge <= 0f )
			blockDelay = DefenseDelay( opponent, move );
		UpdateDodgeWindow( step, opponent, move );
		if ( blockDelay is not { } delay ) return;
		blockDelay = Math.Min( 1f, delay - step );
		if ( blockDelay > 0f ) return;
		blockDelay = null;
		if ( !IsOnGround || RollProgress < 1f || InThrow || Unconscious || currentKnifeLayer is { Active: true } || (strike is not null && blockStun <= 0) ) return;
		activeBlock = 0.2f;
		blockFlinchRaised = 0.3f;
		blockRecharge = 0.4f;
	}

	float? DefenseDelay( RabbitGuard opponent, RabbitAttack move )
	{
		// AnimationReader finds the next prepare event across every pose in the synced group.
		float next = float.PositiveInfinity;
		foreach ( var pose in move.Poses )
		{
			foreach ( var phase in pose.Clip.PreparePhases )
				if ( phase > opponent.AttackPhase ) next = Math.Min( next, phase );
		}
		if ( !float.IsFinite( next ) ) return null;
		float power = 0.5f * MathF.Pow( 4f, opponent.predictability ) + (tactic == CombatTactic.Provoke ? 1f : 0f);
		float chance = 1f - MathF.Pow( 1f - BlockSkill, power );
		if ( !move.AsLayer )
		{
			if ( groupLeader.IsValid() ) chance *= 0.5f;
			chance *= 0.1f + 0.9f * GameDifficulty * GameDifficulty;
		}
		float delay = (next - opponent.AttackPhase) * move.Poses[0].Clip.Duration / (move.AsLayer ? 1f : opponent.AttackSpeed);
		return Random.Shared.Float() > chance ? delay + 0.4f : delay;
	}

	bool TryActiveBlock( RabbitGuard attacker, RabbitAttack move )
	{
		InterruptWeaponLayers();
		if ( boundAnimations is null || !IsOnGround || Unconscious || Health < 1 ) return false;
		if ( boundAnimations.Species == "wolf" && attacker.boundAnimations.Species != "wolf" && WorldScale.z > 0.8f && !move.FleshUnblockable ) return true;
		if ( TryDodge( attacker, move ) ) return false;
		bool active = TrainingThrows || activeBlock > 0f || boundAnimations.Species == "cat" && hitsOnGround > 0;
		if ( !active && (hitsOnGround <= 1 || Random.Shared.Float() <= MathF.Pow( 0.5f, hitsOnGround )) ) return false;
		if ( Downed || IsGetUp( Stance ) || RollProgress < 1f ) throwRecharge = Math.Max( (float)throwRecharge, 1f );
		if ( move.Unblockable || move.FleshUnblockable && (!PrimaryItem.IsValid() || boundAnimations.WeaponLabel == "knife") ) return false;
		var slot = attacker.flippedAttack != MirroredStance ? RabbitMirror.SwapSide( move.Guard ) : move.Guard;
		if ( !boundAnimations.Clips.TryGetValue( slot, out var guard ) ) return false;
		flinching = false;
		flinchFadeOut = 100f;
		if ( Downed || IsGetUp( Stance ) )
		{
			var direction = (CharacterCenter( GameObject ) - CharacterCenter( attacker.GameObject )).Normal;
			var facing = move.WorldImpact( direction, attacker.flippedAttack ).WithZ( 0 );
			if ( Downed )
			{
				if ( !TryGetUp( facing ) ) return false;
			}
			else
			{
				EndRecovery();
				CapturePhysicsPose();
				if ( !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
				StartGetUp( BlockFromGround );
			}
		}
		else
		{
			EndRoll();
			MirroredAction = MirroredStance;
			BeginReaction( attacker.GameObject, attacker.WorldPosition, slot, guard, activeBlock: true );
		}

		PickBlockFollowUp();
		return true;
	}

	bool TryPassiveBlock( RabbitGuard attacker, RabbitAttack move, bool wasUnaware, float sharp )
	{
		float damageMultiplier = 1f / Math.Max( 0.00001f, DamageResistance );
		var species = boundAnimations?.Species;
		var other = attacker.boundAnimations.Species;
		bool wolfAdvantage = species == "wolf" && other != "wolf";
		float blockDamage = Random.Shared.Float( move.BlockMin, move.BlockMax ) * Math.Max( 1f, damageMultiplier ) * Math.Max( 1f, attacker.AttackDamage );
		bool blockingWeapon = PrimaryItem.IsValid() && boundAnimations?.WeaponLabel != "knife" && !wasUnaware;
		bool incomingWeapon = attacker.PrimaryItem.IsValid() && attacker.boundAnimations.WeaponLabel != "knife";
		bool armedParry = incomingWeapon && (blockingWeapon || MetalArmor);
		float cost = 1f;
		if ( other == "dog" && species is not ("dog" or "wolf") ) cost += 0.5f;
		if ( species == "rat" && other != "rat" ) cost += 0.5f;
		bool sizeAdvantage = cost > 1f;
		if ( InAttackState && incomingWeapon && blockingWeapon ) cost += 0.5f;
		if ( move.Special == "legcannon" && !TrainingThrows )
		{
			legCannonHits++;
			blockHealth = 0f;
			if ( species != "wolf" ) KnockoutShield = Math.Max( 0, KnockoutShield - 2 );
			else if ( KnockoutShield == 1 )
			{
				recoverableDamage = Health;
				KnockoutShield = 0;
			}
		}
		blockHealth = Math.Max( wolfAdvantage && move.Special != "legcannon" ? 0.1f : 0f, blockHealth - blockDamage * cost );
		bool guardBroken = blockHealth <= 0f;
		if ( TrainingThrows ) blockHealth = 1f;
		if ( Downed || RollProgress < 1f || boundAnimations is null || Health < 1 || BloodLost >= MaxHealth || !IsOnGround || guardBroken ) return false;
		var direction = (CharacterCenter( GameObject ) - CharacterCenter( attacker.GameObject )).Normal;
		bool canDefend = species == "dog" || (startled <= 0f && (!InAttackState || armedParry) && WorldRotation.Forward.Dot( direction ) <= 0f);
		bool parried = WorldScale.z >= 0.8f && (TrainingThrows || wolfAdvantage || canDefend && (sharp <= 0f || armedParry));
		if ( !parried && (!incomingWeapon || attacker.boundAnimations.WeaponLabel != "spear") ) return false;
		if ( parried && wolfAdvantage && blockStun <= 0f )
		{
			Speak( "block_hit" );
			return true;
		}

		string animation;
		bool reflected;
		if ( species == "wolf" )
		{
			animation = KnockoutShield < initialKnockoutShield / 2 ? "w_slashedrighthard" : "w_slashedright";
			reflected = move.Direction == "left";
		}
		else
		{
			var reaction = boundAnimations.Reactions[move.ReactionPath];
			animation = reaction.AtSeverity( 1f - blockHealth );
			bool mirroredHit = move.Direction != "front" && attacker.flippedAttack;
			reflected = reaction.Mirrored == "maybe" ? MirroredStance : (reaction.Mirrored == "true") != mirroredHit;
		}
		var clip = boundAnimations.Clips[animation];
		MirroredAction = MirroredStance = reflected;
		BeginReaction( attacker.GameObject, attacker.WorldPosition, animation, clip );
		// A spear can force the authored hit reaction even when its damage gets through.
		if ( !parried ) return false;
		if ( sizeAdvantage ) ReceiveBlockImpact( attacker, move, 2f );
		Speak( "block_hit" );
		return true;
	}

	void ReceiveBlockImpact( RabbitGuard attacker, RabbitAttack move, float strength )
	{
		var direction = (CharacterCenter( GameObject ) - CharacterCenter( attacker.GameObject )).Normal;
		var recoil = move.WorldImpact( direction, attacker.flippedAttack ) * strength;
		combatVelocity += recoil.WithZ( 0f ) * (2f * 40f);
		ApplyBlockTilt( recoil * (move.Height == "low" ? -20f : 20f) );
	}

	void BeginReaction( GameObject attacker, Vector3 source, string animation, RabbitClip clip, bool activeBlock = false )
	{
		strike = null;
		StopForCombat();
		var facing = (source - WorldPosition).WithZ( 0 );
		if ( !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
		NoticeAttacker( attacker );
		Stance = animation;
		activeBlockReaction = activeBlock;
		reactionDuration = clip.Duration;
		ReactionPhase = 0;
	}
}
