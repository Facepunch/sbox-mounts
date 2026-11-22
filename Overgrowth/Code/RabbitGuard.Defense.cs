using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property, Range( 0f, 1f )] public float BlockSkill { get; set; } = 0.5f;
	TimeUntil blockRecharge;
	TimeUntil startled;
	float blockHealth = 1f;
	int hitsOnGround;
	float reactionDuration;
	bool activeBlockReaction;
	[Sync( SyncFlags.Interpolate )] float ReactionPhase { get; set; } = 1f;

	bool UpdateDefense()
	{
		blockHealth = Math.Clamp( blockHealth + Time.Delta * 0.3f, 0f, Math.Clamp( Health / 100f, 0f, 1f ) );
		if ( ReactionPhase >= 1f || reactionDuration <= 0f ) return false;
		float elapsed = ReactionPhase * reactionDuration;
		if ( !IsGetUp( Stance ) && (elapsed > (activeBlockReaction ? 0.2f : 0.6f) || elapsed > 0.1f && AnimationCanCancel) )
		{
			ReactionPhase = 1f;
			return false;
		}
		ReactionPhase = Math.Min( 1f, ReactionPhase + Time.Delta / reactionDuration );
		return ReactionPhase < 1f;
	}

	bool TryActiveBlock( RabbitGuard attacker, RabbitAttack move, float predictability )
	{
		if ( boundAnimations is null || !IsOnGround || Health < 1 ) return false;
		if ( !boundAnimations.Clips.TryGetValue( move.Guard, out var guard ) ) return false;
		var skillPower = 0.5f * MathF.Pow( 4f, predictability );
		if ( tactic == CombatTactic.Provoke ) skillPower += 1f;
		bool active = enemy == attacker.GameObject && startled <= 0 && blockRecharge <= 0 && strike is null
			&& Random.Shared.Float() <= 1f - MathF.Pow( 1f - BlockSkill, skillPower );
		if ( !active && (hitsOnGround <= 1 || Random.Shared.Float() <= MathF.Pow( 0.5f, hitsOnGround )) ) return false;
		if ( Downed || IsGetUp( Stance ) )
		{
			var direction = (TargetPosition( GameObject ) - attacker.WorldPosition).Normal;
			var facing = move.WorldImpact( direction ).WithZ( 0 );
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
		else BeginReaction( attacker.GameObject, attacker.WorldPosition, move.Guard, guard, activeBlock: true );
		// At original difficulty an NPC's active window and subsequent recharge are each 0.2 s.
		blockRecharge = 0.4f;
		return true;
	}

	bool TryPassiveBlock( RabbitGuard attacker, RabbitAttack move )
	{
		if ( Downed || IsGetUp( Stance ) ) hitsOnGround++;
		blockHealth = Math.Max( 0f, blockHealth - Random.Shared.Float( move.BlockMin, move.BlockMax ) );
		var direction = (WorldPosition - attacker.WorldPosition).Normal;
		if ( Downed || boundAnimations is null || Health < 1 || !IsOnGround || startled > 0 || strike is not null ||
			blockHealth <= 0 || WorldRotation.Forward.Dot( direction ) > 0 ) return false;

		// Reaction::GetAnimPath partitions the severity range in XML order.
		var animation = move.Reactions[(int)((1f - blockHealth) * 0.999f * move.Reactions.Length)];
		if ( !boundAnimations.Clips.TryGetValue( animation, out var clip ) ) return false;
		BeginReaction( attacker.GameObject, attacker.WorldPosition, animation, clip );
		Speak( "block_hit" );
		return true;
	}

	void BeginReaction( GameObject attacker, Vector3 source, string animation, RabbitClip clip, bool activeBlock = false )
	{
		Movement.Stop();
		var facing = (source - WorldPosition).WithZ( 0 );
		if ( !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
		if ( CanAttack( attacker ) ) Remember( attacker );
		Stance = animation;
		activeBlockReaction = activeBlock;
		reactionDuration = clip.Duration;
		ReactionPhase = 0;
	}
}
