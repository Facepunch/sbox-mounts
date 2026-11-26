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
	enum RestPose { None, Sitting, Sleeping }
	[Sync] RestPose PatrolRest { get; set; }
	bool Sleeping => PatrolRest == RestPose.Sleeping;
	string connectedPatrolAnimation;
	bool IsPatrolGroup => PatrolAnimation?.EndsWith( ".xml", StringComparison.OrdinalIgnoreCase ) == true;

	void UpdatePatrolPose()
	{
		if ( Networking.IsHost && PatrolRest != RestPose.None && (AlertStage == 2 || Investigating || escortTarget.IsValid() || soughtItem.IsValid()) )
		{
			if ( Sleeping && !Downed && !Held && strike is null && ReactionPhase >= 1f )
			{
				var (front, up) = GetTorsoDirections();
				var facing = -up.WithZ( 0 );
				if ( !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
				StartGetUp( front.z < 0f ? StandFromFront : StandFromBack );
			}
			PatrolRest = RestPose.None;
			wokeUp = true;
			waypointAnimation = null;
		}
		if ( Stance != PatrolIdle || string.IsNullOrEmpty( PatrolAnimation ) ) return;
		if ( connectedPatrolAnimation == PatrolAnimation ) return;
		var slot = Renderer.Animation.ExternalGraphs;
		if ( slot.IsConnected( PatrolIdle ) ) slot.Disconnect( PatrolIdle );
		// The parent graph drives the blend coordinates of a patrol group. Single clips play in the external slot.
		if ( !IsPatrolGroup && boundAnimations.PatrolGraph( PatrolAnimation ) is { } graph ) slot.Connect( PatrolIdle, graph );
		connectedPatrolAnimation = PatrolAnimation;
	}

	bool WaitAtPatrolPoint()
	{
		if ( !Sleeping && suspicion > 0f && suspicion > enemySeen )
			PatrolAnimation = suspicion > 0.5f ? "Data/Animations/r_actionidle_listen_b.anm" : "Data/Animations/r_actionidle.anm";
		else if ( !Sleeping && enemySeen > 0f ) return false;
		else if ( waypointAnimation is not null && patrolWait > 0f ) PatrolAnimation = waypointAnimation;
		else return false;
		ReleaseMovementInput();
		Stance = PatrolIdle;
		return true;
	}

	bool FollowPatrol()
	{
		if ( !patrolTarget.IsValid() )
		{
			previousPatrolPoint = null;
			patrolTarget = PatrolStart;
		}
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
		if ( patrolWait > 0f || enemySeen > 0.1f || suspicion > 0.1f ) return WaitAtPatrolPoint();
		if ( !patrolTarget.IsValid() )
		{
			var leader = PatrolCharacter.IsValid() ? PatrolCharacter.GameObject : null;
			if ( PatrolPlayer )
				leader = knownCharacters.Keys.Where( x => x.IsValid() && x.GetComponent<PlayerController>().IsValid() && !IsIgnoredPlayer( x ) )
					.MinBy( x => x.WorldPosition.DistanceSquared( WorldPosition ) );
			if ( !leader.IsValid() || !knownCharacters.ContainsKey( leader ) )
			{
				unassignedPatrol = !PatrolCharacter.IsValid() && !PatrolPlayer;
				return MoveOnPatrol( null, false ) || WaitAtPatrolPoint();
			}
			return MoveOnPatrol( TargetPosition( leader ), true );
		}

		var target = patrolTarget.WorldPosition;
		if ( (target - WorldPosition).WithZ( 0 ).LengthSquared < 40f * 40f )
		{
			var reached = patrolTarget;
			if ( !wokeUp )
			{
				if ( reached.Pose == "Sit" ) PatrolRest = RestPose.Sitting;
				if ( reached.Pose is "Sleep" or "Wounded" ) PatrolRest = RestPose.Sleeping;
			}
			waypointAnimation = wokeUp ? null : reached.Pose switch
			{
				"Sit" => "Data/Animations/r_sit_cross_legged.anm",
				"Sleep" => "Data/Animations/r_sleep.anm",
				"Wounded" => "Data/Animations/r_sit_injuredneck.anm",
				null or "Stand" => null,
				{ } path when IsWaypointAnimation( path ) => path,
				_ => waypointAnimation
			};
			patrolWait = reached.Connections.Count == 0 ? float.PositiveInfinity : reached.Wait;

			patrolTarget = reached.Connections.FirstOrDefault( p => p.IsValid() && p != previousPatrolPoint )
				?? reached.Connections.FirstOrDefault( p => p.IsValid() ) ?? reached;
			previousPatrolPoint = reached;
			if ( StaticCharacter ) return WaitAtPatrolPoint();
		}

		return MoveOnPatrol( target, false );
	}

	static bool IsWaypointAnimation( string path )
	{
		if ( !path.StartsWith( "Data/Animations/", StringComparison.OrdinalIgnoreCase )
			|| Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount || !mount.FileSystem.FileExists( path ) ) return false;
		if ( path.EndsWith( ".anm", StringComparison.OrdinalIgnoreCase ) ) return true;
		return path.EndsWith( ".xml", StringComparison.OrdinalIgnoreCase )
			&& XmlAsset.Parse( mount.FileSystem.ReadAllText( path ) ).Element( "SyncedAnimationGroup" ) is not null;
	}

	bool MoveOnPatrol( Vector3? target, bool following )
	{
		var separation = TeammateSeparation();
		var point = WorldPosition;
		Vector3 intent = default;
		if ( target is { } destination && destination != WorldPosition && moveDelay <= 0f )
		{
			float radius = following ? 40f : 0f;
			intent = MovementToPoint( destination, radius, radius, 0f, out point, separation );
			if ( !following && intent.LengthSquared > 0.2f ) intent = intent.Normal * 0.2f;
		}

		intent = (intent + separation).ClampLength( 1f );
		if ( !separation.IsNearlyZero() ) point = WorldPosition + intent.WithZ( 0f ) * 40f;
		if ( !target.HasValue && intent.IsNearlyZero() ) return false;
		bool moving = MoveWithIntent( point, intent, 1f );
		Stance = moving ? (target.HasValue ? Locomotion : Threat) : Idle;
		return true;
	}
}
