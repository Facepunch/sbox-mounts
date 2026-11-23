namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property] public PatrolPoint PatrolStart { get; set; }
	[Property] public RabbitGuard PatrolCharacter { get; set; }
	[Property] public bool PatrolPlayer { get; set; }
	PatrolPoint previousPatrolPoint;
	PatrolPoint patrolTarget;
	TimeUntil patrolWait;
	internal const string PatrolIdle = "patrol_idle";
	[Sync] string PatrolAnimation { get; set; }
	string waypointAnimation;
	bool wokeUp;
	bool Sleeping => !wokeUp && previousPatrolPoint.IsValid() && previousPatrolPoint.Pose is "Sleep" or "Wounded";
	string connectedPatrolAnimation;

	void UpdatePatrolPose()
	{
		if ( Networking.IsHost && !wokeUp && (AlertStage == 2 || investigatePoint.HasValue || escortTarget.IsValid()) && previousPatrolPoint.IsValid()
			&& previousPatrolPoint.Pose is "Sit" or "Sleep" or "Wounded" )
		{
			if ( Sleeping && !Downed && !Held && strike is null && ReactionPhase >= 1f )
			{
				var (front, up) = GetTorsoDirections();
				var facing = -up.WithZ( 0 );
				if ( !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
				StartGetUp( front.z < 0f ? StandFromFront : StandFromBack );
			}
			wokeUp = true;
			waypointAnimation = null;
		}
		if ( Stance != PatrolIdle || string.IsNullOrEmpty( PatrolAnimation ) ) return;
		if ( connectedPatrolAnimation == PatrolAnimation ) return;
		var slot = Renderer.Animation.ExternalGraphs;
		if ( slot.IsConnected( PatrolIdle ) ) slot.Disconnect( PatrolIdle );
		if ( boundAnimations.PatrolGraph( PatrolAnimation ) is { } graph ) slot.Connect( PatrolIdle, graph );
		connectedPatrolAnimation = PatrolAnimation;
	}

	bool WaitAtPatrolPoint()
	{
		float heard = CurrentSuspicion;
		if ( !Sleeping && heard > 0f && heard > enemySeen )
			PatrolAnimation = heard > 0.5f ? "Data/Animations/r_actionidle_listen_b.anm" : "Data/Animations/r_actionidle.anm";
		else if ( !Sleeping && enemySeen > 0f ) return false;
		else if ( waypointAnimation is not null && patrolWait > 0f ) PatrolAnimation = waypointAnimation;
		else return false;
		if ( Movement.MoveTarget.HasValue ) Movement.Stop();
		Stance = PatrolIdle;
		return true;
	}

	bool FollowPatrol()
	{
		if ( !patrolTarget.IsValid() ) patrolTarget = PatrolStart;
		if ( patrolWait > 0f || enemySeen > 0.1f || CurrentSuspicion > 0.1f ) return WaitAtPatrolPoint();
		if ( !patrolTarget.IsValid() )
		{
			var leader = PatrolCharacter.IsValid() ? PatrolCharacter.GameObject : null;
			if ( PatrolPlayer )
				leader = knownCharacters.Keys.Where( x => x.IsValid() && x.GetComponent<PlayerController>().IsValid() && !IsIgnoredPlayer( x ) )
					.MinBy( x => x.WorldPosition.DistanceSquared( WorldPosition ) );
			if ( !leader.IsValid() || !knownCharacters.ContainsKey( leader ) ) return WaitAtPatrolPoint();
			FollowCharacter( TargetPosition( leader ) );
			return true;
		}

		var target = patrolTarget.WorldPosition;
		if ( (target - WorldPosition).WithZ( 0 ).LengthSquared < 40f * 40f )
		{
			var reached = patrolTarget;
			waypointAnimation = wokeUp ? null : reached.Pose switch
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

		if ( Movement.MoveTarget != target ) Movement.MoveTo( target, 0f, 0.2f * GroundRunSpeed );
		Stance = Locomotion;
		return true;
	}
}
