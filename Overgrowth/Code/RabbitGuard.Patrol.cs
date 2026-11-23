namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property] public PatrolPoint PatrolStart { get; set; }
	PatrolPoint previousPatrolPoint;
	PatrolPoint patrolTarget;
	TimeUntil patrolWait;
	internal const string PatrolIdle = "patrol_idle";
	[Sync] string PatrolAnimation { get; set; }
	bool wokeUp;
	string connectedPatrolAnimation;

	void UpdatePatrolPose()
	{
		if ( Networking.IsHost && (AlertStage == 2 || investigatePoint.HasValue || escortTarget.IsValid()) && previousPatrolPoint.IsValid()
			&& previousPatrolPoint.Pose is "Sit" or "Sleep" or "Wounded" )
			wokeUp = true;
		if ( Stance != PatrolIdle || string.IsNullOrEmpty( PatrolAnimation ) ) return;
		if ( connectedPatrolAnimation == PatrolAnimation ) return;
		var slot = Renderer.Animation.ExternalGraphs;
		if ( slot.IsConnected( PatrolIdle ) ) slot.Disconnect( PatrolIdle );
		if ( boundAnimations.PatrolGraph( PatrolAnimation ) is { } graph ) slot.Connect( PatrolIdle, graph );
		connectedPatrolAnimation = PatrolAnimation;
	}

	bool WaitAtPatrolPoint()
	{
		if ( wokeUp || PatrolAnimation is null || patrolWait <= 0f || enemySeen > 0f ) return false;
		if ( Movement.MoveTarget.HasValue ) Movement.Stop();
		Stance = PatrolIdle;
		return true;
	}

	bool FollowPatrol()
	{
		if ( !patrolTarget.IsValid() ) patrolTarget = PatrolStart;
		if ( !patrolTarget.IsValid() ) return false;
		if ( patrolWait > 0f || enemySeen > 0.1f ) return WaitAtPatrolPoint();

		var target = patrolTarget.WorldPosition;
		if ( (target - WorldPosition).WithZ( 0 ).LengthSquared < 40f * 40f )
		{
			var reached = patrolTarget;
			PatrolAnimation = reached.Pose switch
			{
				"Sit" => "Data/Animations/r_sit_cross_legged.anm",
				"Sleep" => "Data/Animations/r_sleep.anm",
				"Wounded" => "Data/Animations/r_sit_injuredneck.anm",
				{ } path when path.StartsWith( "Data/Animations/", System.StringComparison.OrdinalIgnoreCase ) && path.EndsWith( ".anm", System.StringComparison.OrdinalIgnoreCase ) => path,
				_ => null
			};
			if ( reached.Wait is { } wait )
				patrolWait = reached.Connections.Count == 0 ? float.PositiveInfinity : wait;

			patrolTarget = reached.Connections.FirstOrDefault( p => p.IsValid() && p != previousPatrolPoint )
				?? reached.Connections.FirstOrDefault( p => p.IsValid() ) ?? reached;
			previousPatrolPoint = reached;
			return WaitAtPatrolPoint();
		}

		if ( Movement.MoveTarget != target ) Movement.MoveTo( target, 0f, 0.2f * 8f * 40f );
		Stance = Locomotion;
		return true;
	}
}
