using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[ConVar( "overgrowth_difficulty", ConVarFlags.Replicated, Help = "Overgrowth AI difficulty from 0 (easiest) to 1 (original)." )]
	public static float Difficulty { get; set; } = 1f;
	static float GameDifficulty => Math.Clamp( Difficulty, 0f, 1f );

	enum CombatTactic { Rush, Wait, Defend, Provoke }
	[Property, Range( 0f, 1f )] public float Aggression { get; set; } = 0.5f;
	[Property, Range( 0f, 1f )] public float GroundAggression { get; set; } = 0.5f;
	[Property, Range( 0f, 1f )] public float BlockFollowUp { get; set; } = 0.5f;
	CombatTactic tactic = CombatTactic.Wait;
	bool? punishGroundedTarget;
	TimeSince sinceTargetDown;
	bool rangeChanged = true;
	TimeUntil nextTactic;
	TimeUntil dontAttackBefore;
	float preferredRange;
	float strafeOffset;
	bool UsesPolearm => boundAnimations.WeaponLabel is "staff" or "spear";

	void PickBlockFollowUp()
	{
		float chance = BlockFollowUp;
		if ( tactic == CombatTactic.Provoke ) chance = 1f - (1f - chance) * (1f - chance);
		if ( Random.Shared.Float() >= chance ) return;
		wantsBlockThrow = Random.Shared.Float() > 0.7f;
		if ( wantsBlockThrow ) return;
		rangeChanged |= tactic != CombatTactic.Rush;
		tactic = CombatTactic.Rush;
		nextTactic = 1f;
	}

	enum AirResponse { None, Hold, Evade, Counter, KnockOffLedge }
	AirResponse airResponse;
	GameObject airborneOpponent;

	bool RespondToAirborneTarget( float distance )
	{
		if ( boundAnimations.Species != "wolf" && enemy.GetComponent<RabbitGuard>() is { OnLedge: true, Downed: false } )
		{
			airResponse = AirResponse.KnockOffLedge;
			airborneOpponent = null;
			preferredRange = 0f;
			return true;
		}
		bool airborne = (GameDifficulty > 0.25f || (followerCount == 0 && !groupLeader.IsValid())) && boundAnimations.Species != "wolf" && enemy.GetComponent<RabbitGuard>() is not { Downed: true } && TargetIsAirborne( enemy );
		if ( !airborne )
		{
			if ( airResponse != AirResponse.None ) nextTactic = 0f;
			airResponse = AirResponse.None;
			airborneOpponent = null;
			return false;
		}
		if ( airborneOpponent != enemy )
		{
			airborneOpponent = enemy;
			float skill = 1f - 0.9f * MathF.Pow( 1f - GameDifficulty, 2f );
			airResponse = Random.Shared.Float() < BlockSkill * skill ? AirResponse.Evade :
				Random.Shared.Float() < Aggression * skill ? AirResponse.Counter : AirResponse.Hold;
			rangeChanged = true;
		}
		if ( airResponse == AirResponse.Counter ) preferredRange = 0f;
		if ( airResponse == AirResponse.Evade )
		{
			if ( rangeChanged || Random.Shared.Float() < Time.Delta * 120f / 150f )
				preferredRange = Random.Shared.Float( 3f, 4f ) * 40f;
			if ( distance * distance < 8f * 40f * 40f && RollProgress >= 1f )
			{
				strafeOffset = strafeOffset > 0f ? 200f : -200f;
				var direction = CombatApproach( TargetPosition( enemy ), out _ );
				if ( direction.LengthSquared > 0.2f )
				{
					StartRoll( direction, Animation.MoveVelocity, GroundMaterialName );
					breathSpeed += 1f;
				}
			}
		}
		rangeChanged = false;
		return true;
	}

	void UpdateTactic( float distance )
	{
		if ( Random.Shared.Float() < Time.Delta * 120f / 150f )
			strafeOffset = Random.Shared.Float( -0.2f, 0.2f ) * 40f;
		if ( RespondToAirborneTarget( distance ) ) return;
		if ( nextTactic <= 0 )
		{
			nextTactic = GameDifficulty >= 0.25f ? Random.Shared.Float( 0.35f, 1.25f ) : 0f;
			var next = CombatTactic.Defend;
			if ( GameDifficulty < 0.25f || boundAnimations.Species == "wolf" ) next = CombatTactic.Rush;
			else if ( Random.Shared.Float() < Aggression + (!groupLeader.IsValid() && followerCount > 0 ? 0.125f : 0f) )
			{
				next = Random.Shared.Float() > 0.5f ? CombatTactic.Wait : CombatTactic.Rush;
				var opponent = enemy.GetComponent<RabbitGuard>();
				if ( !UsesPolearm && opponent.IsValid() && opponent.PrimaryItem.IsValid()
					&& (opponent.strike?.AsLayer != false || opponent.strikeTarget == GameObject) )
				{
					if ( opponent.boundAnimations.WeaponLabel == "knife" || (!PrimaryItem.IsValid() && (throwRecharge > 0f || sinceDodgeAttempt < 3f)) )
						next = CombatTactic.Rush;
				}
			}

			// PickAttackSubGoal commits to one ground-punish decision until the next attack
			// or until the target has been out of ragdoll for more than 0.8 seconds.
			if ( enemy.GetComponent<RabbitGuard>() is { Downed: true } )
			{
				sinceTargetDown = 0;
				punishGroundedTarget ??= Random.Shared.Float() < GroundAggression * GameDifficulty;
			}
			else if ( sinceTargetDown > 0.8f ) punishGroundedTarget = null;
			if ( distance > 4f * 40f || (next == CombatTactic.Rush && punishGroundedTarget == false)
				|| ((groupLeader.IsValid() || dontAttackBefore > 0f) && next is CombatTactic.Rush or CombatTactic.Wait) )
				next = CombatTactic.Provoke;

			rangeChanged |= next != tactic;
			tactic = next;
		}

		// CheckRangeChange rolls once per AI update with the number of 120 Hz steps elapsed.
		if ( rangeChanged || Random.Shared.Float() < Time.Delta * 120f / 150f )
		{
			rangeChanged = false;
			preferredRange = tactic switch
			{
				CombatTactic.Defend or CombatTactic.Wait => Random.Shared.Float( 1.5f, 3f ) * 40f,
				CombatTactic.Rush when UsesPolearm => 2f * 40f,
				_ => 0f
			};
		}
	}
}
