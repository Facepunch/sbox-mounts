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

	void UpdateDodgeWindow( float step )
	{
		if ( dodgeDelay is not { } delay ) return;
		dodgeDelay = Math.Min( 1f, delay - step );
		if ( dodgeDelay > 0f ) return;
		dodgeDelay = null;
		if ( dodgeRecharge > 0f || !InMovementState || !IsOnGround || !enemy.IsValid() ) return;
		dodgeDirection = (WorldPosition - enemy.WorldPosition).Normal;
		dodgeWindow = dodgeRecharge = 0.2f;
	}

	bool TryDodge( RabbitGuard attacker, RabbitAttack move )
	{
		if ( boundAnimations.Species == "wolf" || startled > 0f || RollProgress < 1f || nextSuccessfulDodge > 0f || sinceDodgeAttempt < 0.5f ) return false;
		bool forced = InMovementState && move.FleshUnblockable &&
			(attacker.boundAnimations.WeaponLabel == "knife" ? Random.Shared.Next( 3 ) == 0 :
			(!PrimaryItem.IsValid() || Random.Shared.Next( 10 ) == 0) && tactic == CombatTactic.Provoke);
		if ( !(forced && Random.Shared.Float() <= GameDifficulty) && dodgeWindow <= 0f ) return false;

		sinceDodgeAttempt = 0f;
		var toward = (attacker.WorldPosition - WorldPosition).WithZ( 0f ).Normal;
		if ( Downed || toward.Dot( dodgeDirection ) > 0.85f ) return false;
		var animation = move.Height switch { "high" => DodgeHigh, "medium" => DodgeMiddle, _ => DodgeLow };
		MirroredAction = MirroredStance;
		BeginReaction( attacker.GameObject, WorldPosition - dodgeDirection, animation, boundAnimations.Clips[animation] );
		dodgeWindow = 0f;
		nextSuccessfulDodge = 3f;
		wantsBlockThrow = true;
		attacker.stunnedBy = this;
		attacker.blockStun = attacker.boundAnimations.Species == "wolf" ? 1f : 0.5f;
		return true;
	}
}
