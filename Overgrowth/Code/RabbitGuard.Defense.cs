using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property, Range( 0f, 1f )] public float BlockSkill { get; set; } = 0.5f;
	TimeUntil blockRecharge, activeBlock;
	float? blockDelay;
	TimeUntil startled;
	float blockHealth = 1f;
	int hitsOnGround;
	float duckAmount, duckVelocity;
	float reactionDuration;
	bool activeBlockReaction;
	[Sync( SyncFlags.Interpolate )] float ReactionPhase { get; set; } = 1f;

	bool UpdateDefense()
	{
		// UpdateDuckAmount runs in movement and ground recovery. Unarmed combat AI rises toward standing.
		if ( strike is null && (ReactionPhase >= 1f || IsGetUp( Stance )) )
		{
			duckVelocity -= duckAmount * Time.Delta * 120f;
			duckVelocity *= MathF.Pow( 0.89f, Time.Delta * 120f );
			duckAmount += duckVelocity * Time.Delta;
		}
		blockHealth = Math.Clamp( blockHealth + Time.Delta * 0.3f, 0f, Math.Clamp( Health / 100f, 0f, 1f ) );
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
		return ReactionPhase < 1f;
	}

	void UpdateActiveBlock( float step )
	{
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
				blockDelay = (next - opponent.AttackPhase) * move.Poses[0].Clip.Duration;
				if ( Random.Shared.Float() > 1f - MathF.Pow( 1f - BlockSkill, power ) ) blockDelay += 0.4f;
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
		if ( boundAnimations is null || !IsOnGround || Health < 1 ) return false;
		var slot = attacker.flippedAttack != MirroredStance ? RabbitMirror.SwapSide( move.Guard ) : move.Guard;
		if ( !boundAnimations.Clips.TryGetValue( slot, out var guard ) ) return false;
		bool active = activeBlock > 0;

		if ( !active && (hitsOnGround <= 1 || Random.Shared.Float() <= MathF.Pow( 0.5f, hitsOnGround )) ) return false;
		if ( Downed || IsGetUp( Stance ) )
		{
			throwRecharge = Math.Max( (float)throwRecharge, 1f );
			var direction = (TargetPosition( GameObject ) - attacker.WorldPosition).Normal;
			var facing = move.WorldImpact( direction, attacker.flippedAttack ).WithZ( 0 );
			if ( Downed )
			{
				if ( !TryGetUp( facing ) ) return false;
			}
			else
			{
				EndGetUpBlend();
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
		blockHealth = Math.Max( 0f, blockHealth - Random.Shared.Float( move.BlockMin, move.BlockMax ) );
		var direction = (WorldPosition - attacker.WorldPosition).Normal;
		if ( Downed || boundAnimations is null || Health < 1 || !IsOnGround || startled > 0 || strike is not null ||
			blockHealth <= 0 || WorldRotation.Forward.Dot( direction ) > 0 ) return false;

		// Reaction::GetAnimPath partitions the severity range in XML order.
		var animation = move.Reactions[(int)((1f - blockHealth) * 0.999f * move.Reactions.Length)];
		if ( !boundAnimations.Clips.TryGetValue( animation, out var clip ) ) return false;
		bool mirroredReaction = move.Direction != "front" && attacker.flippedAttack;
		MirroredAction = MirroredStance = move.ReactionMirror == "maybe" ? MirroredStance : (move.ReactionMirror == "true") != mirroredReaction;
		BeginReaction( attacker.GameObject, attacker.WorldPosition, animation, clip );
		Speak( "block_hit" );
		return true;
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
