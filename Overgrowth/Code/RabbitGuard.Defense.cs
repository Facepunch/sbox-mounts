using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	float blockHealth = 1f;
	float reactionDuration;
	[Sync( SyncFlags.Interpolate )] float ReactionPhase { get; set; } = 1f;

	bool UpdateDefense()
	{
		blockHealth = Math.Clamp( blockHealth + Time.Delta * 0.3f, 0f, Math.Clamp( Health / 100f, 0f, 1f ) );
		if ( ReactionPhase >= 1f || reactionDuration <= 0f ) return false;
		ReactionPhase = Math.Min( 1f, ReactionPhase + Time.Delta / reactionDuration );
		return ReactionPhase < 1f;
	}

	bool TryPassiveBlock( RabbitGuard attacker, RabbitAttack move )
	{
		blockHealth = Math.Max( 0f, blockHealth - Random.Shared.Float( move.BlockMin, move.BlockMax ) );
		var direction = (WorldPosition - attacker.WorldPosition).Normal;
		if ( boundAnimations is null || Health < 1 || !IsOnGround || strike is not null ||
			blockHealth <= 0 || WorldRotation.Forward.Dot( direction ) > 0 ) return false;

		// Reaction::GetAnimPath partitions the severity range in XML order.
		var animation = move.Reactions[(int)((1f - blockHealth) * 0.999f * move.Reactions.Length)];
		if ( !boundAnimations.Clips.TryGetValue( animation, out var clip ) ) return false;
		Movement.Stop();
		var facing = -direction.WithZ( 0 );
		if ( !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
		Remember( attacker.GameObject );
		Stance = animation;
		reactionDuration = clip.Duration;
		ReactionPhase = 0;
		Speak( "block_hit" );
		return true;
	}
}
