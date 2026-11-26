using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string DodgeHigh = "r_dodgebackhigh", DodgeMiddle = "r_dodgebackmid", DodgeLow = "r_dodgebacklow";
	internal static bool IsDodge( string stance ) => stance is DodgeHigh or DodgeMiddle or DodgeLow;
	bool IsDodging => ReactionPhase < 1f && IsDodge( Stance );
	bool InMovementState => !Downed && !InThrow && RollProgress >= 1f && ReactionPhase >= 1f && strike?.AsLayer != false;

	float? dodgeDelay;
	TimeUntil dodgeRecharge, dodgeWindow, nextSuccessfulDodge = 3f;
	TimeSince sinceDodgeAttempt = 0f;
	Vector3 dodgeDirection;

	float dodgeRemainder;

	void UpdateDodgeWindow( float delta, RabbitGuard opponent, RabbitAttack move )
	{
		const float tick = 4f / 120f;
		dodgeRemainder += delta;
		while ( dodgeRemainder >= tick )
		{
			dodgeRemainder -= tick;
			if ( WantsToDodge() && dodgeRecharge <= 0f && InMovementState && IsOnGround && !IsTethered && enemy.IsValid() )
			{
				dodgeDirection = (CharacterCenter( GameObject ) - CharacterCenter( enemy )).Normal;
				dodgeWindow = 0.2f;
			}
			if ( WantsToDodge() ) dodgeRecharge = 0.2f;
		}

		bool WantsToDodge()
		{
			if ( dodgeDelay is null && move is { AsLayer: true } && dodgeRecharge <= 0f )
				dodgeDelay = DefenseDelay( opponent, move );
			if ( dodgeDelay is not { } delay ) return false;
			dodgeDelay = Math.Min( 1f, delay - tick );
			if ( dodgeDelay > 0f ) return false;
			dodgeDelay = null;
			return true;
		}
	}

	bool TryDodge( RabbitGuard attacker, RabbitAttack move )
	{
		if ( boundAnimations.Species == "wolf" || startled > 0f || RollProgress < 1f || nextSuccessfulDodge > 0f || sinceDodgeAttempt < 0.5f ) return false;
		bool forced = InMovementState && move.FleshUnblockable &&
			(attacker.PrimaryItem?.Label == "knife" ? Random.Shared.Next( 3 ) == 0 :
			(!PrimaryItem.IsValid() || Random.Shared.Next( 10 ) == 0) && tactic == CombatTactic.Provoke);
		if ( !(forced && Random.Shared.Float() <= GameDifficulty) && dodgeWindow <= 0f ) return false;

		sinceDodgeAttempt = 0f;
		var toward = (TargetPosition( attacker.GameObject ) - WorldPosition).WithZ( 0f ).Normal;
		if ( Downed || toward.Dot( dodgeDirection ) > 0.85f ) return false;
		var animation = move.Height switch { "high" => DodgeHigh, "medium" => DodgeMiddle, _ => DodgeLow };
		MirroredAction = MirroredStance;
		BeginReaction( attacker.GameObject, WorldPosition - dodgeDirection, animation, boundAnimations.Clips[animation] );
		dodgeWindow = 0f;
		nextSuccessfulDodge = 3f;
		wantsBlockThrow = true;
		attacker.dontAttackBefore = Random.Shared.Float( 0f, 1.4f );
		attacker.stunnedBy = this;
		attacker.blockStun = attacker.boundAnimations.Species == "wolf" ? 1f : 0.5f;
		return true;
	}
}
