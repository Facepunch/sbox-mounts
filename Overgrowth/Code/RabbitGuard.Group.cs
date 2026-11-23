using System;
using Sandbox.Npcs;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	RabbitGuard groupLeader;
	int followerCount;

	void FollowLeader( RabbitGuard leader )
	{
		if ( groupLeader == leader ) return;
		if ( groupLeader.IsValid() ) groupLeader.followerCount--;
		groupLeader = leader;
		if ( leader.IsValid() ) leader.followerCount++;
	}

	bool CanLead( RabbitGuard ally ) => ally != this && ally.Active && !ally.Downed && !ally.Unconscious
		&& ally.Health > 0 && ally.HasCombatTarget && GetDisposition( ally.GameObject ) == Disposition.Friendly;

	void PassLeadership()
	{
		var next = Scene.GetAllComponents<RabbitGuard>().FirstOrDefault( CanLead );
		if ( !next.IsValid() ) return;
		next.FollowLeader( null );
		FollowLeader( next );
	}

	void UpdateCombatGroup()
	{
		// Spawn-menu fighters have no team. Authored teammates take turns engaging, as in AllyInfo.
		if ( Teams.Length == 0 && !groupLeader.IsValid() && followerCount == 0 ) return;
		if ( !HasCombatTarget )
		{
			if ( followerCount > 0 ) PassLeadership();
			FollowLeader( null );
			return;
		}

		const float range = 3f * 40f;
		var target = TargetPosition( enemy );
		if ( groupLeader.IsValid() )
		{
			if ( groupLeader.Unconscious || groupLeader.Downed || !groupLeader.Active || groupLeader.Health <= 0 ||
				groupLeader.groupLeader.IsValid() || groupLeader.WorldPosition.DistanceSquared( target ) > range * range )
				FollowLeader( null );
		}
		else if ( followerCount == 0 )
		{
			foreach ( var ally in Scene.GetAllComponents<RabbitGuard>() )
			{
				if ( !CanLead( ally ) || ally.groupLeader.IsValid() || ally.WorldPosition.DistanceSquared( target ) >= range * range ) continue;
				FollowLeader( ally );
				break;
			}
		}
		else if ( Random.Shared.Float() < 1f - MathF.Pow( 0.99f, Time.Delta * 30f ) ) PassLeadership();
	}

	void EndGroupAttack()
	{
		if ( followerCount > 0 && !groupLeader.IsValid() && Random.Shared.Next( 2 ) == 0 ) PassLeadership();
	}
}
