using Sandbox.Engine.Resources;
using Sandbox.Npcs.Layers;
using System;

namespace Overgrowth;

public sealed class CharacterNavigation : NavigationLayer
{
	bool followingJump;
	Vector3? preparedVelocity;
	TimeUntil takeoff, checkRoute;
	Vector3? predictedClimb;
	[Property] public bool StickToNavMesh { get; set; }
	TimeSince sinceAirborne = 0f;

	protected override bool UsesGround => Npc is not RabbitGuard { OnLedge: true } && base.UsesGround;

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( Npc.IsAirborne ) sinceAirborne = 0f;
		if ( Npc is RabbitGuard climber && climber.AdvanceLedge() ) return;
		var crossing = LinkTraversal;
		if ( Npc is RabbitGuard fighter )
		{
			if ( !crossing.HasValue && !followingJump ) CheckPartialRoute( fighter );
			bool climb = predictedClimb.HasValue || crossing?.Type == NavMeshLinkType.Climb;
			if ( climb || crossing?.Type == NavMeshLinkType.Jump )
			{
				var end = predictedClimb ?? crossing.Value.End;
				if ( sinceAirborne <= 0.1f ) return;
				if ( !preparedVelocity.HasValue )
				{
					Vector3 velocity;
					if ( !(climb ? fighter.TryClimbLaunch( end, out velocity ) : TryJumpVelocity( WorldPosition, end, out velocity )) )
					{
						if ( crossing.HasValue ) AbortLinkTraversal();
						predictedClimb = null;
						return;
					}
					preparedVelocity = velocity;
					takeoff = fighter.PrepareJump();
				}
				if ( takeoff > 0f ) return;
				if ( climb ) fighter.StartLedgeApproach( preparedVelocity.Value );
				Npc.Launch( preparedVelocity.Value );
				predictedClimb = null;
				followingJump = true;
			}
		}
		preparedVelocity = null;
		base.OnUpdate();
		if ( !followingJump ) return;
		if ( !Npc.IsAirborne ) { followingJump = false; return; }
		Npc.Animation.Traversal = NavMeshLinkType.Jump;
	}

	void CheckPartialRoute( RabbitGuard fighter )
	{
		if ( StickToNavMesh || !fighter.IsOnGround || !Agent.IsValid() || MoveTarget is not { } goal )
		{
			predictedClimb = null;
			return;
		}
		if ( predictedClimb.HasValue || checkRoute > 0f ) return;
		checkRoute = 0.1f;
		// Only copy the corridor when its end is within the takeoff radius.
		if ( (Agent.GetLookAhead( 40f ) - WorldPosition).WithZ( 0f ).LengthSquared >= 800f ) return;
		var path = Agent.GetPath();
		if ( path.Status != Sandbox.Navigation.NavMeshPathStatus.Partial || path.Points.Count == 0 ) return;
		var edge = path.Points[^1].Position;
		if ( edge.DistanceSquared( goal ) <= 1600f || (edge - WorldPosition).WithZ( 0f ).LengthSquared >= 800f ) return;
		if ( fighter.PredictClimb( edge, out var end ) )
		{
			predictedClimb = end;
			checkRoute = 2f;
		}
	}

	protected override void OnDisabled()
	{
		followingJump = false;
		preparedVelocity = null;
		if ( Npc is RabbitGuard fighter ) fighter.EndLedge();
		predictedClimb = null;
		base.OnDisabled();
	}

	// GetVelocityForTarget bounds the flight time by running speed and maximum launch height.
	// JumpToTarget tries the lowest arc first and accepts the first collision near the destination.
	internal bool TryJumpVelocity( Vector3 start, Vector3 end, out Vector3 velocity )
	{
		velocity = default;
		const float maxVertical = 5f * 1.7f * 40f;
		const float radius = 0.45f * 40f;
		const float toleranceSquared = 0.5f * 40f * 40f;
		float gravity = -Scene.PhysicsWorld.Gravity.z * RabbitGuard.FallGravityScale( Scene.PhysicsWorld );
		var delta = end - start;
		if ( Npc is not RabbitGuard fighter ) return false;
		start += Vector3.Up * radius;
		end += Vector3.Up * radius;
		for ( int arc = 0; arc < 3; arc++ )
		{
			var launch = FlightPath.Velocity( delta, gravity, fighter.GroundRunSpeed * 1.5f, maxVertical, arc == 0 ? 0.15f : arc == 1 ? 0.55f : 1f, out _ );
			if ( launch.IsNearlyZero() ) return false;
			// A touching Box3D sweep reports the takeoff floor as an initial overlap.
			var from = start + Vector3.Up * 0.1f;
			for ( int i = 1; i <= 400; i++ )
			{
				float t = i * (20f / 120f);
				var to = start + launch * t + Vector3.Down * (0.5f * gravity * t * t);
				var hit = Scene.Trace.Sphere( radius, from, to ).IgnoreGameObjectHierarchy( GameObject )
					.IgnoreDynamic().WithoutTags( "trigger", "player", "npc" ).Run();
				if ( hit.Hit )
				{
					if ( !hit.StartedSolid && hit.EndPosition.DistanceSquared( end ) < toleranceSquared )
					{
						velocity = launch;
						return true;
					}
					break;
				}
				if ( to.z < end.z - MathF.Sqrt( toleranceSquared ) && to.z < from.z ) break;
				from = to;
			}
		}
		return false;
	}
}
