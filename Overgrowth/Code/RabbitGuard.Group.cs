using System;
using Sandbox.Npcs;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	RabbitGuard groupLeader;
	int followerCount;
	TimeUntil moveDelay, backAwayUntil;
	readonly HashSet<GameObject> nearbyTeammates = new();

	Vector3 TeammateSeparation()
	{
		if ( Teams.Length == 0 && !AlliedToPlayer ) return default;
		const float range = 1.5f * 40f;
		var center = CharacterCenter( GameObject );
		Vector3 force = default;
		nearbyTeammates.Clear();
		foreach ( var hit in Scene.FindInPhysics( new Sphere( center, range ) ) )
		{
			var ally = hit.GetComponentInParent<RabbitGuard>();
			var remains = hit.GetComponentInParent<CharacterRemains>();
			var teammate = ally?.GameObject ?? remains?.GameObject;
			if ( teammate is null && AlliedToPlayer ) teammate = hit.GetComponentInParent<PlayerController>()?.GameObject;
			if ( !teammate.IsValid() || teammate == GameObject || !nearbyTeammates.Add( teammate ) ) continue;
			var teams = ally?.Teams ?? remains?.Teams;
			if ( teams is not null && !Teams.Any( teams.Contains ) ) continue;

			var away = center - CharacterCenter( teammate );
			float distance = away.Length;
			if ( distance <= 0f || distance >= range ) continue;
			force += away * ((range - distance) / (range * distance));
			if ( moveDelay <= 0f )
			{
				if ( ally.IsValid() ) ally.moveDelay = 1f;
				backAwayUntil = 1f;
			}
		}
		var flat = force.WithZ( 0f );
		if ( flat.IsNearlyZero() ) return default;

		var clipped = WalkableRay( WorldPosition, WorldPosition + flat * 40f );
		float reachable = (clipped - WorldPosition).WithZ( 0f ).Length;
		return flat * (reachable / (40f * force.Length));
	}

	bool unassignedPatrol;
	internal bool IsBackingAway => unassignedPatrol && backAwayUntil > 0f;

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
		if ( Unconscious || Health <= 0f || !HasCombatTarget )
		{
			if ( followerCount > 0 ) PassLeadership();
			FollowLeader( null );
			return;
		}

		const float range = 3f * 40f;
		var target = CharacterCenter( enemy );
		if ( groupLeader.IsValid() )
		{
			if ( groupLeader.Unconscious || groupLeader.Downed || !groupLeader.Active || groupLeader.Health <= 0 ||
				groupLeader.groupLeader.IsValid() || CharacterCenter( groupLeader.GameObject ).DistanceSquared( target ) > range * range )
				FollowLeader( null );
		}
		else if ( followerCount == 0 )
		{
			foreach ( var ally in Scene.GetAllComponents<RabbitGuard>() )
			{
				if ( !CanLead( ally ) || ally.groupLeader.IsValid() || CharacterCenter( ally.GameObject ).DistanceSquared( target ) >= range * range ) continue;
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
