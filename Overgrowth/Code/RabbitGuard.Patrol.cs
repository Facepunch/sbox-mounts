namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property] public PatrolPoint PatrolStart { get; set; }
	PatrolPoint previousPatrolPoint;
	PatrolPoint patrolTarget;
	TimeUntil patrolWait;

	bool FollowPatrol()
	{
		if ( !patrolTarget.IsValid() ) patrolTarget = PatrolStart;
		if ( !patrolTarget.IsValid() || patrolWait > 0f || enemySeen > 0.1f ) return false;

		var target = patrolTarget.WorldPosition;
		if ( (target - WorldPosition).WithZ( 0 ).LengthSquared < 40f * 40f )
		{
			var reached = patrolTarget;
			if ( reached.Wait is { } wait )
				patrolWait = reached.Connections.Count == 0 ? float.PositiveInfinity : wait;

			patrolTarget = reached.Connections.FirstOrDefault( p => p.IsValid() && p != previousPatrolPoint )
				?? reached.Connections.FirstOrDefault( p => p.IsValid() ) ?? reached;
			previousPatrolPoint = reached;
			return false;
		}

		if ( Movement.MoveTarget != target ) Movement.MoveTo( target, 0f, 0.2f * 8f * 40f );
		Stance = Locomotion;
		return true;
	}
}
