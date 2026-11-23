using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property, Range( 0f, 1f )] public float BlockSkill { get; set; } = 0.5f;
	TimeUntil blockRecharge, activeBlock;
	float? blockDelay;
	float flinchSpring, flinchVelocity, flinchFade, flinchFadeOut, flinchOpacity;
	bool flinching;
	[Sync] bool MirroredFlinch { get; set; }
	[Sync( SyncFlags.Interpolate )] float FlinchWeight { get; set; }

	TimeUntil startled;
	float blockHealth = 1f;
	int hitsOnGround;
	float duckAmount, duckVelocity;
	float reactionDuration;
	bool activeBlockReaction;
	Vector3 blockVelocity;
	[Sync( SyncFlags.Interpolate )] float ReactionPhase { get; set; } = 1f;

	void UpdateBlockFlinch()
	{
		float dt = Time.Delta;
		// ActiveBlocking lasts .2 seconds; the spring stays raised for .3 from activation.
		bool raised = activeBlock > -0.1f;
		float target = raised ? 1f : plantDepth * Math.Min( 1f, Animation.MoveVelocity.Length / 40f );
		flinchVelocity += (target - flinchSpring) * dt * (raised ? 300f : 150f);
		flinchSpring += dt * flinchVelocity;
		flinchVelocity *= MathF.Pow( raised ? 0.8f : 0.86f, dt * 120f );
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
		if ( flinching ) flinchOpacity = Math.Clamp( flinchSpring, 0.00001f, 1f );
		flinchFade = Math.Clamp( flinchFade + dt * (flinching ? 10f : -flinchFadeOut), 0f, 1f );
		FlinchWeight = flinchFade * flinchOpacity;
	}

	bool InspectingBody => inspectUntil.HasValue && !enemy.IsValid() && (casualty.IsValid() || inspectPosition.HasValue);

	bool UpdateDefense()
	{
		// WantsToCrouch is held while checking a body, including the approach settling to a stop.
		if ( strike is not { AsLayer: false } && (ReactionPhase >= 1f || IsGetUp( Stance )) )
		{
			float speed = Animation.MoveVelocity.WithZ( 0f ).Length / 40f;
			float target = InspectingBody ? 1f - 0.7f * Math.Min( 1f, speed / 5f ) : 0f;
			if ( draggedBody.IsValid() && DragStrength < 0.7f ) target = 1f;
			if ( currentKnifeLayer is { Active: true } && strikeTarget.IsValid() )
				target = Math.Max( target, Math.Clamp( -GetAttackHeight( strikeTarget ), 0f, 1f ) );
			AdvanceCrouch( target );
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
		var clip = boundAnimations.Clips[Stance].ForSide( MirroredAction ).Animation;
		var from = clip.GetRootMotion( ReactionPhase );
		ReactionPhase = Math.Min( 1f, ReactionPhase + Time.Delta / reactionDuration );
		ApplyRootMotion( from, clip.GetRootMotion( ReactionPhase ) );
		if ( IsGetUp( Stance ) && ReactionPhase >= 1f )
		{
			duckAmount = CrouchAmount = 1f;
			duckVelocity = 0f;
		}
		return ReactionPhase < 1f;
	}

	void AdvanceCrouch( float target )
	{
		duckVelocity += (target - duckAmount) * Time.Delta * 120f;
		duckVelocity *= MathF.Pow( 0.89f, Time.Delta * 120f );
		duckAmount += duckVelocity * Time.Delta;
	}

	void UpdateActiveBlock( float step )
	{
		if ( boundAnimations.Species == "wolf" ) return;
		if ( startled <= 0f && HasCombatTarget && enemy.GetComponent<RabbitGuard>() is { strike: { } move } opponent && opponent.Stance != move.Blocked )
		{
			if ( move.AsLayer && dodgeDelay is null && dodgeRecharge <= 0f ) dodgeDelay = DefenseDelay( opponent, move );
			if ( !move.AsLayer && blockDelay is null && blockRecharge <= 0f ) blockDelay = DefenseDelay( opponent, move );
		}
		UpdateDodgeWindow( step );
		if ( blockDelay is not { } delay ) return;
		blockDelay = Math.Min( 1f, delay - step );
		if ( blockDelay > 0f ) return;
		blockDelay = null;
		if ( !IsOnGround || InThrow || Unconscious || currentKnifeLayer is { Active: true } || (strike is not null && blockStun <= 0) ) return;
		activeBlock = 0.2f;
		blockRecharge = 0.4f;
	}

	float? DefenseDelay( RabbitGuard opponent, RabbitAttack move )
	{
		// AnimationReader finds the next prepare event across every pose in the synced group.
		float next = float.PositiveInfinity;
		foreach ( var pose in move.Poses )
		{
			if ( pose.Clip.Prepare is not { } prepare ) continue;
			float phase = prepare.Time / pose.Clip.Duration;
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
		blockVelocity += recoil.WithZ( 0f ) * (2f * 40f);
		ApplyBlockTilt( recoil * (move.Height == "low" ? -20f : 20f) );
	}

	void AdvanceBlockRecoil( float dt )
	{
		if ( blockVelocity.LengthSquared < 0.000001f ) return;
		// UpdateHitReaction damps velocity before the four-tick movement step.
		float retention = MathF.Pow( 0.95f, 4f );
		float remaining = MathF.Pow( retention, dt * 30f );
		MoveGroundDelta( blockVelocity * (retention * (1f - remaining) / (30f * (1f - retention))) );
		blockVelocity *= remaining;
	}

	void BeginReaction( GameObject attacker, Vector3 source, string animation, RabbitClip clip, bool activeBlock = false )
	{
		strike = null;
		Movement.Stop();
		var facing = (source - WorldPosition).WithZ( 0 );
		if ( !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
		NoticeAttacker( attacker );
		Stance = animation;
		activeBlockReaction = activeBlock;
		reactionDuration = clip.Duration;
		ReactionPhase = 0;
	}
}
