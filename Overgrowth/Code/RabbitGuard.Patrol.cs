using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property] public PatrolPoint PatrolStart { get; set; }
	[Property] public RabbitGuard PatrolCharacter { get; set; }
	[Property] public bool PatrolPlayer { get; set; }
	PatrolPoint previousPatrolPoint;
	PatrolPoint patrolTarget;
	TimeUntil patrolWait;
	Transform? patrolOrigin;
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
		if ( patrolWait > 0f && patrolTarget.IsValid() && patrolTarget.Connections.Count == 0 && patrolOrigin is { } origin )
		{
			var offset = origin.Position - WorldPosition;
			if ( offset.LengthSquared < 120f * 120f )
			{
				float steps = Time.Delta * 30f;
				MoveGroundDelta( offset.WithZ( 0 ) * (1f - MathF.Pow( 0.95f, steps )) );
				var facing = Vector3.Lerp( origin.Rotation.Forward, WorldRotation.Forward, MathF.Pow( 0.9f, steps ) ).WithZ( 0 );
				if ( !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
			}
		}
		if ( patrolWait > 0f || enemySeen > 0.1f || CurrentSuspicion > 0.1f ) return WaitAtPatrolPoint();
		if ( !patrolTarget.IsValid() )
		{
			var leader = PatrolCharacter.IsValid() ? PatrolCharacter.GameObject : null;
			if ( PatrolPlayer )
				leader = knownCharacters.Keys.Where( x => x.IsValid() && x.GetComponent<PlayerController>().IsValid() && !IsIgnoredPlayer( x ) )
					.MinBy( x => x.WorldPosition.DistanceSquared( WorldPosition ) );
			if ( !leader.IsValid() || !knownCharacters.ContainsKey( leader ) )
			{
				unassignedPatrol = !PatrolCharacter.IsValid() && !PatrolPlayer;
				if ( TryPatrolSeparation( null, false ) ) return true;
				return WaitAtPatrolPoint();
			}
			var position = TargetPosition( leader );
			if ( TryPatrolSeparation( position, true ) ) return true;
			FollowCharacter( position );
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

		if ( TryPatrolSeparation( target, false ) ) return true;
		if ( Movement.MoveTarget != target ) Movement.MoveTo( target, 0f, 0.2f * GroundRunSpeed );
		Stance = Locomotion;
		return true;
	}

	bool TryPatrolSeparation( Vector3? target, bool following )
	{
		var separation = TeammateSeparation();
		if ( separation.IsNearlyZero() && yieldToTeammate <= 0f ) return false;
		Vector3 intent = default;
		if ( target.HasValue && yieldToTeammate <= 0f )
		{
			var delta = (target.Value - WorldPosition).WithZ( 0f ) / 40f;
			if ( following ) delta = delta.Normal * Math.Max( 0f, delta.Length - 2f );
			intent = (delta + separation).ClampLength( 1f );
			if ( !following && intent.LengthSquared > 0.2f ) intent = intent.Normal * 0.2f;
		}
		// GetPatrolMovement adds separation again after limiting the path-following speed.
		intent += separation;
		float speed = Math.Min( 1f, intent.Length );
		if ( speed > 0f )
		{
			Movement.MoveTo( WorldPosition + intent.Normal * (speed * 40f), 0f, speed * GroundRunSpeed );
			Stance = target.HasValue ? Locomotion : Threat;
		}
		else
		{
			Movement.Stop();
			Stance = Idle;
		}
		return true;
	}
}
