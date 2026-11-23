using Sandbox.Engine.Resources;
using Sandbox.Npcs.Layers;
using System;

namespace Overgrowth;

public sealed class CharacterNavigation : NavigationLayer
{
	bool followingJump;
	Vector3? preparedVelocity;
	TimeUntil takeoff, checkRoute;
	Vector3? predictedClimb, runOffDirection;
	float runOffSpeed;
	float requestedGroundSpeed;
	TimeUntil runOffTimeout;
	[Property] public bool StickToNavMesh { get; set; }
	TimeSince sinceAirborne = 0f;

	protected override bool UsesGround => !runOffDirection.HasValue && Npc is not RabbitGuard { OnLedge: true } && base.UsesGround;

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;
		requestedGroundSpeed = 0f;
		if ( runOffDirection.HasValue && AdvanceRunOff() ) return;
		if ( Npc.IsAirborne ) sinceAirborne = 0f;
		if ( Npc is RabbitGuard climber && climber.AdvanceLedge() ) return;
		var crossing = LinkTraversal;
		if ( Npc is RabbitGuard fighter )
		{
			if ( !crossing.HasValue && !followingJump ) CheckPartialRoute( fighter );
			if ( runOffDirection.HasValue ) return;
			bool climb = predictedClimb.HasValue || crossing?.Type == NavMeshLinkType.Climb;
			if ( climb || crossing?.Type == NavMeshLinkType.Jump )
			{
				var end = predictedClimb ?? crossing.Value.End;
				if ( sinceAirborne <= 0.1f ) return;
				if ( !preparedVelocity.HasValue )
				{
					Vector3 velocity;
					if ( !fighter.CanLaunchJump( climb ) || !(climb ? fighter.TryClimbLaunch( end, out velocity ) : TryJumpVelocity( WorldPosition, end, out velocity )) )
					{
						if ( crossing.HasValue ) AbortLinkTraversal();
						predictedClimb = null;
						return;
					}
					preparedVelocity = velocity;
					if ( !climb ) fighter.AnnounceTargetedJump();
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
		var facing = WorldRotation.Forward;
		base.OnUpdate();
		if ( Npc is RabbitGuard moving && Agent.IsValid() && !Npc.IsAirborne )
		{
			var flat = Agent.Velocity.WithZ( 0f );
			if ( moving.IsBackingAway && flat.LengthSquared > 0.001f * 40f * 40f )
				WorldRotation = RabbitGuard.InterpolateFacing( facing, -flat.Normal, 1f - MathF.Pow( 0.95f, Time.Delta * 120f ) );
			else if ( moving.StanceFacing( facing, flat.Length ) is { } stanceFacing )
				WorldRotation = stanceFacing;
			else if ( moving.InLocomotion && flat.Length > 0.6f * 40f )
				WorldRotation = RunFacing( facing, flat.Normal, Agent.WishVelocity.WithZ( 0f ) / moving.GroundRunSpeed, Time.Delta );
		}
		if ( !Npc.IsAirborne && !crossing.HasValue && MoveTarget.HasValue && Agent is { Active: true } )
			requestedGroundSpeed = Agent.MaxSpeed;
		if ( !followingJump ) return;
		if ( !Npc.IsAirborne ) { followingJump = false; return; }
		Npc.Animation.Traversal = NavMeshLinkType.Jump;
	}

	protected override void OnFixedUpdate()
	{
		if ( Networking.IsHost && requestedGroundSpeed > 0f && MoveTarget.HasValue && !Npc.IsAirborne &&
			Agent is { Active: true } && !IsTraversingLink && Npc is RabbitGuard fighter )
			fighter.UpdateGroundSpeed( Agent, requestedGroundSpeed, Time.Delta );
	}

	// UpdateAnimation turns toward actual travel at ten radians per second. When the desired
	// route is behind the character it leads the turn, even while the body is still coasting.
	internal static Rotation RunFacing( Vector3 facing, Vector3 travel, Vector3 intent, float delta )
	{
		var look = intent + travel * 0.1f;
		bool reversing = facing.Dot( look ) < 0f;
		var target = reversing ? look : travel;
		// Measure angles in the authored X/Z plane to retain the turn direction at a half-turn.
		float angle = MathF.Atan2( facing.x, facing.y );
		float turn = MathF.Atan2( target.x, target.y ) - angle;
		if ( turn > MathF.PI ) turn -= MathF.Tau;
		else if ( turn < -MathF.PI ) turn += MathF.Tau;
		if ( reversing ) turn = Math.Clamp( turn, -0.6f * MathF.PI, 0.6f * MathF.PI );
		angle += Math.Clamp( turn, -10f * delta, 10f * delta );
		return Rotation.LookAt( new Vector3( MathF.Sin( angle ), MathF.Cos( angle ), 0f ) );
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
		if ( !fighter.PredictTraversal( edge, goal, out var end, out bool drop ) ) return;
		checkRoute = 2f;
		if ( drop )
		{
			var direction = (end - WorldPosition).WithZ( 0f ).Normal;
			runOffSpeed = Agent.MaxSpeed;
			runOffTimeout = 2f;
			Npc.Launch( direction * runOffSpeed );
			runOffDirection = direction;
		}
		else predictedClimb = end;
	}

	bool AdvanceRunOff()
	{
		var body = Npc.GetComponent<Rigidbody>();
		if ( runOffTimeout > 0f && body.IsValid() && Npc.Health > 0 && !Npc.PhysicsCarrier.IsValid() )
		{
			var start = body.PhysicsBody.Position + Vector3.Up * 4f;
			var support = Scene.Trace.Ray( start, start + Vector3.Down * 12f ).IgnoreGameObjectHierarchy( Npc.GameObject ).WithoutTags( "trigger", "npc", "player" ).Run();
			if ( support.Hit && !support.StartedSolid )
			{
				body.Velocity = (runOffDirection.Value * runOffSpeed).WithZ( body.Velocity.z );
				return true;
			}
		}
		runOffDirection = null;
		return false;
	}

	protected override void OnDisabled()
	{
		followingJump = false;
		preparedVelocity = null;
		if ( Npc is RabbitGuard fighter ) fighter.EndLedge();
		predictedClimb = null;
		runOffDirection = null;
		base.OnDisabled();
	}

	// GetVelocityForTarget bounds the flight time by running speed and maximum launch height.
	// JumpToTarget tries the lowest arc first and accepts the first collision near the destination.
	internal bool TryJumpVelocity( Vector3 start, Vector3 end, out Vector3 velocity )
	{
		velocity = default;
		const float radius = 0.45f * 40f;
		const float toleranceSquared = 0.5f * 40f * 40f;
		float gravity = -Scene.PhysicsWorld.Gravity.z * RabbitGuard.FallGravityScale( Scene.PhysicsWorld );
		var delta = end - start;
		if ( Npc is not RabbitGuard fighter ) return false;
		float maxVertical = fighter.JumpInitialVelocity * 1.7f * 40f;
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
