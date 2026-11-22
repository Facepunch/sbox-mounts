using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	enum CombatTactic { Rush, Wait, Defend, Provoke }
	[Property, Range( 0f, 1f )] public float Aggression { get; set; } = 0.5f;
	CombatTactic tactic;
	TimeUntil nextTactic;
	float preferredRange;

	void UpdateTactic( float distance )
	{
		if ( nextTactic > 0 ) return;
		nextTactic = Random.Shared.Float( 0.35f, 1.25f );
		if ( distance > 4f * 40f ) tactic = CombatTactic.Provoke;
		else if ( Random.Shared.Float() >= Aggression ) tactic = CombatTactic.Defend;
		else tactic = Random.Shared.Float() > 0.5f ? CombatTactic.Wait : CombatTactic.Rush;
		preferredRange = tactic is CombatTactic.Defend or CombatTactic.Wait ? Random.Shared.Float( 1.5f, 3f ) * 40f : 0f;
	}
}
