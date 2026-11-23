using System;
using Sandbox.Npcs;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	RabbitGuard groupLeader;
	int followerCount;
	TimeUntil yieldToTeammate, backAwayUntil;
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
			if ( yieldToTeammate <= 0f )
			{
				if ( ally.IsValid() ) ally.yieldToTeammate = 1f;
				backAwayUntil = 1f;
			}
		}
		var flat = force.WithZ( 0f );
		if ( flat.IsNearlyZero() ) return default;

		// The public nav query returns corridor corners. Only the straight, reachable
		// portion of this short step contributes to the original repulsor force.
		var end = WorldPosition + flat * 40f;
		var limited = flat * (flat.Length / force.Length);
		if ( Scene.NavMesh.GetClosestEdge( WorldPosition, flat.Length * 40f ) is null ) return limited;
		bool Reachable( float fraction )
		{
			var target = Vector3.Lerp( WorldPosition, end, fraction );
			var path = Scene.NavMesh.CalculatePath( new() { Start = WorldPosition, Target = target } );
			return path.IsValid && path.Points.Count <= 2 && path.Points.Count > 0 &&
				(path.Points[^1].Position - target).WithZ( 0f ).LengthSquared < 0.0001f;
		}
		if ( Reachable( 1f ) ) return limited;
		float low = 0f, high = 1f;
		while ( (high - low) * flat.Length * 40f > 0.05f )
		{
			float mid = (low + high) * 0.5f;
			if ( Reachable( mid ) ) low = mid;
			else high = mid;
		}
		return limited * low;
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
