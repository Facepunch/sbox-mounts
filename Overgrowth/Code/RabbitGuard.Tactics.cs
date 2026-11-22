using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	enum CombatTactic { Rush, Wait, Defend, Provoke }
	[Property, Range( 0f, 1f )] public float Aggression { get; set; } = 0.5f;
	[Property, Range( 0f, 1f )] public float GroundAggression { get; set; } = 0.5f;
	CombatTactic tactic = CombatTactic.Wait;
	bool? punishGroundedTarget;
	TimeSince sinceTargetDown;
	bool rangeChanged = true;
	TimeUntil nextTactic;
	TimeUntil dontAttackBefore;
	float preferredRange;
	float strafeOffset;

	void UpdateTactic( float distance )
	{
		if ( Random.Shared.Float() < Time.Delta * 120f / 150f )
			strafeOffset = Random.Shared.Float( -0.2f, 0.2f ) * 40f;
		if ( nextTactic <= 0 )
		{
			nextTactic = Random.Shared.Float( 0.35f, 1.25f );
			var next = CombatTactic.Defend;
			if ( Random.Shared.Float() < Aggression )
				next = Random.Shared.Float() > 0.5f ? CombatTactic.Wait : CombatTactic.Rush;

			// PickAttackSubGoal commits to one ground-punish decision until the next attack
			// or until the target has been out of ragdoll for more than 0.8 seconds.
			if ( enemy.GetComponent<RabbitGuard>() is { Downed: true } )
			{
				sinceTargetDown = 0;
				punishGroundedTarget ??= Random.Shared.Float() < GroundAggression;
			}
			else if ( sinceTargetDown > 0.8f ) punishGroundedTarget = null;
			if ( distance > 4f * 40f || (next == CombatTactic.Rush && punishGroundedTarget == false)
				|| (dontAttackBefore > 0f && next is CombatTactic.Rush or CombatTactic.Wait) )
				next = CombatTactic.Provoke;

			rangeChanged |= next != tactic;
			tactic = next;
		}

		// CheckRangeChange rolls once per AI update with the number of 120 Hz steps elapsed.
		if ( rangeChanged || Random.Shared.Float() < Time.Delta * 120f / 150f )
		{
			rangeChanged = false;
			preferredRange = tactic is CombatTactic.Defend or CombatTactic.Wait ? Random.Shared.Float( 1.5f, 3f ) * 40f : 0f;
		}
	}
}
