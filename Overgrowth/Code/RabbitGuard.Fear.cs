using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property, Group( "Fear" )] public bool CausesFearOnSight { get; set; }
	[Property, Group( "Fear" )] public bool AlwaysAfraidOnSight { get; set; }
	[Property, Group( "Fear" )] public bool NeverAfraidOnSight { get; set; }
	[Property, Group( "Fear" ), Range( 0f, 1f )] public float AfraidAtHealthLevel { get; set; }

	GameObject fleeFrom;
	TimeUntil fleeUpdate;
	Vector3? fleeDestination;

	bool HasCombatTarget => !Fleeing && IsKnownThreat( enemy );
	bool HasAttackGoal => HasCombatTarget && !soughtItem.IsValid() && (!holdPartner.IsValid() || Holding);
	bool Fleeing => fleeFrom.IsValid() && fleeFrom == enemy;

	bool Flee()
	{
		if ( !IsKnownThreat( enemy ) ) { fleeFrom = null; return false; }
		if ( !Fleeing && Health <= MaxHealth * AfraidAtHealthLevel ) fleeFrom = enemy;
		if ( !Fleeing ) return false;
		if ( startled > 0f )
		{
			ReleaseMovementInput();
			Stance = Ready;
			return true;
		}
		if ( fleeUpdate <= 0f )
		{
			fleeUpdate = Random.Shared.Float( 0.2f, 2f );
			var away = (CharacterCenter( GameObject ) - CharacterCenter( enemy )).WithZ( 0f ).Normal;
			fleeDestination = away.IsNearlyZero() ? null : RetreatDestination( away );
		}
		if ( fleeDestination is { } point ) MoveTowardPoint( point, 0f, 1f );
		else ReleaseMovementInput();
		Stance = Locomotion;
		return true;
	}

	Vector3? RetreatDestination( Vector3 away )
	{
		var nav = Scene.NavMesh;
		if ( nav.GetClosestPoint( WorldPosition ) is not { } start ) return null;
		return SlideOnNavMesh( start, start + away * 40000f );
	}
}
