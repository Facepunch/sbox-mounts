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
		float target = raised ? 1f : 0f;
		flinchVelocity += (target - flinchSpring) * dt * (raised ? 300f : 150f);
		flinchSpring += dt * flinchVelocity;
		flinchVelocity *= MathF.Pow( raised ? 0.8f : 0.86f, dt * 120f );
		bool settled = MathF.Abs( flinchSpring ) < 0.01f && MathF.Abs( flinchVelocity ) < 0.01f;
		if ( flinching && settled )
		{
			flinching = false;
			flinchFadeOut = 4f;
		}
		if ( !flinching && !settled && ReactionPhase >= 1f && !InThrow && !Downed && NeedsCombatPose() )
		{
			flinching = true;
			MirroredFlinch = MirroredStance;
		}
		if ( flinching ) flinchOpacity = Math.Clamp( flinchSpring, 0.00001f, 1f );
		flinchFade = Math.Clamp( flinchFade + dt * (flinching ? 10f : -flinchFadeOut), 0f, 1f );
		FlinchWeight = flinchFade * flinchOpacity;
	}

	bool UpdateDefense()
	{
		// UpdateDuckAmount runs in movement and ground recovery. Unarmed combat AI rises toward standing.
		if ( strike is null && (ReactionPhase >= 1f || IsGetUp( Stance )) )
		{
			duckVelocity -= duckAmount * Time.Delta * 120f;
			duckVelocity *= MathF.Pow( 0.89f, Time.Delta * 120f );
			duckAmount += duckVelocity * Time.Delta;
		}
		if ( ReactionPhase >= 1f || reactionDuration <= 0f ) return false;
		float elapsed = ReactionPhase * reactionDuration;
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

	void UpdateActiveBlock( float step )
	{
		if ( boundAnimations.Species == "wolf" ) return;
		if ( blockDelay is null && startled <= 0 && blockRecharge <= 0 && CanAttack( enemy ) &&
			enemy.GetComponent<RabbitGuard>() is { strike: { } move } opponent && opponent.Stance != move.Blocked )
		{
			// AnimationReader searches all synced poses for the next event, using the first pose as its clock.
			float next = float.PositiveInfinity;
			foreach ( var pose in move.Poses )
			{
				if ( pose.Clip.Prepare is not { } prepare ) continue;
				float phase = prepare.Time / pose.Clip.Duration;
				if ( phase > opponent.AttackPhase ) next = Math.Min( next, phase );
			}
			if ( float.IsFinite( next ) )
			{
				float power = 0.5f * MathF.Pow( 4f, opponent.predictability );
				if ( tactic == CombatTactic.Provoke ) power += 1f;
				blockDelay = (next - opponent.AttackPhase) * move.Poses[0].Clip.Duration / opponent.AttackSpeed;
				float chance = 1f - MathF.Pow( 1f - BlockSkill, power );
				if ( groupLeader.IsValid() ) chance *= 0.5f;
				chance *= 0.1f + 0.9f * GameDifficulty * GameDifficulty;
				if ( Random.Shared.Float() > chance ) blockDelay += 0.4f;
			}
		}
		if ( blockDelay is not { } delay ) return;
		blockDelay = Math.Min( 1f, delay - step );
		if ( blockDelay > 0f ) return;
		blockDelay = null;
		if ( !IsOnGround || InThrow || (strike is not null && blockStun <= 0) ) return;
		activeBlock = 0.2f;
		blockRecharge = 0.4f;
	}

	bool TryActiveBlock( RabbitGuard attacker, RabbitAttack move )
	{
		if ( boundAnimations is null || !IsOnGround || Unconscious || Health < 1 ) return false;
		if ( boundAnimations.Species == "wolf" && attacker.boundAnimations.Species != "wolf" && WorldScale.z > 0.8f && !move.FleshUnblockable ) return true;
		if ( move.Unblockable || move.FleshUnblockable && (!PrimaryItem.IsValid() || boundAnimations.WeaponLabel == "knife") ) return false;
		var slot = attacker.flippedAttack != MirroredStance ? RabbitMirror.SwapSide( move.Guard ) : move.Guard;
		if ( !boundAnimations.Clips.TryGetValue( slot, out var guard ) ) return false;
		bool active = activeBlock > 0;

		if ( !active && (hitsOnGround <= 1 || Random.Shared.Float() <= MathF.Pow( 0.5f, hitsOnGround )) ) return false;
		flinching = false;
		flinchFadeOut = 100f;
		if ( Downed || IsGetUp( Stance ) )
		{
			throwRecharge = Math.Max( (float)throwRecharge, 1f );
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
			MirroredAction = MirroredStance;
			BeginReaction( attacker.GameObject, attacker.WorldPosition, slot, guard, activeBlock: true );
		}

		PickBlockFollowUp();
		return true;
	}

	bool TryPassiveBlock( RabbitGuard attacker, RabbitAttack move )
	{
		float damageMultiplier = 1f / Math.Max( 0.00001f, DamageResistance );
		var species = boundAnimations?.Species;
		var other = attacker.boundAnimations.Species;
		bool wolfAdvantage = species == "wolf" && other != "wolf";
		float blockDamage = Random.Shared.Float( move.BlockMin, move.BlockMax ) * Math.Max( 1f, damageMultiplier ) * Math.Max( 1f, attacker.AttackDamage );
		float cost = 1f;
		if ( other == "dog" && species is not ("dog" or "wolf") ) cost += 0.5f;
		if ( species == "rat" && other != "rat" ) cost += 0.5f;
		blockHealth = Math.Max( wolfAdvantage && move.Special != "legcannon" ? 0.1f : 0f, blockHealth - blockDamage * cost );
		if ( Downed || RollProgress < 1f || boundAnimations is null || Health < 1 || !IsOnGround || WorldScale.z < 0.8f || blockHealth <= 0f ) return false;
		var direction = (CharacterCenter( GameObject ) - CharacterCenter( attacker.GameObject )).Normal;
		if ( !wolfAdvantage && species != "dog" && (startled > 0 || strike is not null || WorldRotation.Forward.Dot( direction ) > 0f) ) return false;
		if ( wolfAdvantage && blockStun <= 0f )
		{
			Speak( "block_hit" );
			return true;
		}

		// Reaction::GetAnimPath partitions the severity range in XML order.
		var animation = move.Reactions[(int)((1f - blockHealth) * 0.999f * move.Reactions.Length)];
		if ( !boundAnimations.Clips.TryGetValue( animation, out var clip ) ) return false;
		bool mirroredReaction = move.Direction != "front" && attacker.flippedAttack;
		MirroredAction = MirroredStance = move.ReactionMirror == "maybe" ? MirroredStance : (move.ReactionMirror == "true") != mirroredReaction;
		BeginReaction( attacker.GameObject, attacker.WorldPosition, animation, clip );
		if ( cost > 1f ) ReceiveBlockImpact( attacker, move, 2f );
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
