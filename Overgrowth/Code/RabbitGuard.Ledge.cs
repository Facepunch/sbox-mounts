using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string ClimbUp = "r_ledge_climb_fast", LedgeReach = "r_ledge_barely_reach";
	[Sync] string LedgeAnimation { get; set; }
	[Sync] bool LedgeMirrored { get; set; }
	[Sync] float LedgeHeight { get; set; }
	[Sync] Vector3 LedgeGrabOrigin { get; set; }
	[Sync] float LedgeGrabTime { get; set; }
	[Sync( SyncFlags.Interpolate )] float LedgeHeightOffset { get; set; }
	[Sync( SyncFlags.Interpolate )] float LedgePhase { get; set; }
	Vector3 climbDirection, climbPosition;
	internal bool OnLedge => LedgeAnimation is not null;
	float jumpFuel, climbAirClock;
	TimeSince sinceClimbLaunch;
	bool approachingLedge, leavingLedge;
	Vector3 climbApproachVelocity;
	float climbStartDuck, climbStartDuckVelocity;
	int climbGroundTicks, climbWindupTicks;
	const float ClimbStep = 4f / 120f;
	internal const float LegRadius = 18f;

	SceneTrace ClimbTrace( SceneTrace trace ) => trace.IgnoreGameObjectHierarchy( GameObject ).IgnoreDynamic().WithoutTags( "npc", "player", "trigger" );

	internal float BeginClimbPreparation( Vector3 end, float delay )
	{
		climbApproachVelocity = BodyVelocity;
		climbStartDuck = duckAmount;
		climbStartDuckVelocity = duckVelocity;
		climbGroundTicks = 0;
		climbWindupTicks = (int)MathF.Ceiling( delay / ClimbStep );
		AdvanceClimbPreparation( end );
		return climbWindupTicks * ClimbStep;
	}

	// Ground controls still run on each crouch tick, including the tick which releases the jump.
	internal Vector3 AdvanceClimbPreparation( Vector3 end )
	{
		const float tick = ClimbStep;
		int due = Math.Min( climbWindupTicks, 1 + (int)((float)SinceJumpPreparation / tick) );
		var normal = runGroundNormal.IsNearlyZero() ? FloorNormal() : runGroundNormal;
		var heading = GroundDirection( (end - WorldPosition).WithZ( 0f ).Normal, normal );
		Vector3 DriveAt( int index ) => heading * (tick * GroundDrive( CrouchSpringAt( climbStartDuck, climbStartDuckVelocity, groundCrouchTarget, index * tick ).Amount ));
		while ( climbGroundTicks < due )
		{
			groundSpeedLimit = Math.Min( GroundRunSpeed * 1.5f, Math.Max( climbApproachVelocity.Length * 0.98f, GroundRunSpeed * (1f - 0.7f * heading.z) ) );
			climbApproachVelocity = (climbApproachVelocity + DriveAt( climbGroundTicks++ )) * MathF.Exp( -GroundAcceleration * tick / groundSpeedLimit );
			MoveGroundDelta( climbApproachVelocity * tick, out var travel );
			climbApproachVelocity = travel / tick;
		}
		Animation.SetMove( climbApproachVelocity );
		return DriveAt( climbWindupTicks );
	}

	internal bool TryClimbLaunch( Vector3 destination, out Vector3 velocity )
	{
		velocity = default;
		var direction = (destination - WorldPosition).WithZ( 0f ).Normal;
		if ( boundAnimations is null || direction.IsNearlyZero() || destination.z <= WorldPosition.z ) return false;
		var normal = runGroundNormal.IsNearlyZero() ? FloorNormal() : runGroundNormal;
		float launchSpeed = JumpInitialVelocity * 40f;
		float limit = groundSpeedLimit > 0f ? groundSpeedLimit : GroundRunSpeed;
		float forwardSpeed = GroundRunSpeed * Math.Min( 1f, BodyVelocity.Length / limit + 0.2f );
		velocity = direction * forwardSpeed + Vector3.Up * launchSpeed;
		if ( velocity.Normal.Dot( normal ) < 0.3f )
		{
			var right = normal.Cross( direction ).WithZ( 0f ).Normal;
			var front = right.Cross( normal ).Normal;
			velocity = front * forwardSpeed + front.Cross( right ).Normal * launchSpeed;
		}
		velocity.z = Math.Min( velocity.z, launchSpeed );
		return true;
	}

	internal void StartLedgeApproach( Vector3 launch )
	{
		approachingLedge = true;
		climbDirection = launch.WithZ( 0f ).Normal;
		sinceClimbLaunch = 0f;
		jumpFuel = JumpSustain * (boundAnimations.CharacterSpecies == "rabbit" ? 1f : 0.2f);
		climbAirClock = 0f;
	}

	Vector3 AdvanceClimbVelocity( Vector3 velocity, float delta )
	{
		while ( climbAirClock >= ClimbStep )
		{
			climbAirClock -= ClimbStep;
			velocity += climbDirection * (JumpAirControl * 40f * ClimbStep);
			if ( jumpFuel <= 0f || velocity.z <= 0f ) continue;
			float burn = JumpSustainBoost * ClimbStep;
			jumpFuel -= burn;
			velocity.z += burn * 40f;
		}
		climbAirClock += delta;
		return velocity;
	}

	bool GetLedgeHeight( Vector3 center, Vector3 wall, out float height, out Surface surface )
	{
		height = 0f;
		surface = null;
		var over = center + wall * 20f;
		var down = ClimbTrace( Scene.Trace.Cylinder( 40f, LegRadius, over + Vector3.Up * 100f, over + Vector3.Up * 20f ) ).UseHitPosition().Run();
		if ( !down.Hit || down.StartedSolid ) return false;
		var clear = down.EndPosition + Vector3.Up * 2.8f;
		if ( ClimbTrace( Scene.Trace.Cylinder( 40f, LegRadius, clear, clear ) ).Run().Hit ) return false;
		height = down.EndPosition.z - 20f;
		surface = TerrainLoader.ContactSurface( down, down.HitPosition );
		return true;
	}

	internal bool PredictTraversal( Vector3 edge, Vector3 goal, out Vector3 destination, out bool drop )
	{
		destination = default;
		var center = edge + Vector3.Up * 100f;
		var contacts = ClimbTrace( Scene.Trace.Sphere( 80f, center, center ) ).RunAll();
		drop = !contacts.Any();
		if ( drop )
		{
			var direction = (goal - WorldPosition).WithZ( 0f ).Normal;
			var over = center + direction * 160f;
			var landing = ClimbTrace( Scene.Trace.Sphere( LegRadius, over, over + Vector3.Down * 4000f ) ).Run();
			if ( !landing.Hit || landing.StartedSolid || landing.HitPosition.z >= WorldPosition.z ) return false;
			destination = landing.EndPosition.WithZ( WorldPosition.z );
			return true;
		}
		foreach ( var contact in contacts.OrderBy( hit => hit.HitPosition.DistanceSquared( center ) ) )
		{
			var wall = (contact.HitPosition - center).WithZ( 0f ).Normal;
			if ( wall.IsNearlyZero() ) continue;
			var foot = (contact.HitPosition - wall * LegRadius).WithZ( WorldPosition.z + LegRadius );
			if ( !GetLedgeHeight( foot, wall, out float height, out _ ) ) continue;
			destination = (WorldPosition + wall * 40f).WithZ( height );
			return height > WorldPosition.z;
		}
		return false;
	}

	bool FindLedgeDirection( Vector3 center, Vector3 previous, out Vector3 wall, out SceneTraceResult contact )
	{
		contact = default;
		wall = default;
		for ( int i = 0; i < 3; i++ )
		{
			var origin = center + Vector3.Up * (12f * i);
			if ( i == 0 ) origin -= previous * 4f;
			float best = float.PositiveInfinity;
			float nearest = float.PositiveInfinity;
			foreach ( var hit in ClimbTrace( Scene.Trace.Sphere( LegRadius * 1.5f, origin, origin ) ).RunAll() )
			{
				var offset = hit.HitPosition - origin;
				float distance = offset.LengthSquared;
				if ( distance < nearest )
				{
					contact = hit;
					nearest = distance;
				}
				if ( offset.Normal.z < -0.7f || offset.WithZ( 0f ).IsNearlyZero() || distance >= best ) continue;
				wall = offset.WithZ( 0f ).Normal;
				best = distance;
			}
			if ( nearest < float.PositiveInfinity )
			{
				if ( best == float.PositiveInfinity ) wall = (contact.HitPosition - origin).WithZ( 0f ).Normal;
				return true;
			}
		}
		return false;
	}

	bool FindLedge( ref Vector3 center, float verticalSpeed, out Vector3 wall, out float height, out Surface surface, out SceneTraceResult contact )
	{
		height = 0f;
		surface = null;
		if ( !FindLedgeDirection( center, default, out wall, out contact ) ) return false;
		if ( !GetLedgeHeight( center, wall, out height, out surface ) )
		{
			// CheckLedges retries away from the first surface at inside corners.
			var previous = wall;
			if ( !FindLedgeDirection( center, previous, out wall, out contact ) ||
				!GetLedgeHeight( center - previous * 8f, wall, out height, out surface ) ) return false;
			center -= previous * 4f;
		}
		if ( wall.IsNearlyZero() ) return false;
		var rim = center.WithZ( height );
		var outside = rim - wall * 4f;
		if ( ClimbTrace( Scene.Trace.Sphere( LegRadius, outside, outside + Vector3.Down * 20f ) ).Run().Hit ) return false;
		if ( !ClimbTrace( Scene.Trace.Cylinder( 40f, LegRadius, rim, rim + wall * 40f ) ).Run().Hit ) return false;
		float reach = verticalSpeed >= 60f ? 40f : verticalSpeed > 0f ? 100f : 60f;
		return center.z > height - reach;
	}

	internal bool AdvanceLedge()
	{
		if ( !approachingLedge && !leavingLedge && LedgeAnimation is null ) return false;
		if ( Downed || PhysicsCarrier.IsValid() || Health < 1f ) { EndLedge(); return false; }
		var body = GetComponent<Rigidbody>();
		var physics = body?.PhysicsBody;
		if ( !physics.IsValid() ) { EndLedge(); return false; }
		if ( leavingLedge )
		{
			if ( IsOnGround || Movement.MoveTarget is null ) { leavingLedge = false; return false; }
			var center = physics.Position + Vector3.Up * LegRadius;
			var support = ClimbTrace( Scene.Trace.Sphere( LegRadius, center + Vector3.Up * 4f, center + Vector3.Down * 8f ) ).Run();
			if ( !support.Hit || support.Normal.z < 0.5f ) { leavingLedge = false; return false; }
			var step = climbDirection - support.Normal * climbDirection.Dot( support.Normal );
			body.Velocity = step * (0.2f * GroundRunSpeed);
			return true;
		}
		if ( LedgeAnimation is null )
		{
			if ( sinceClimbLaunch > 2f || IsOnGround && sinceClimbLaunch > 0.1f ) { EndLedge(); return false; }
			var velocity = body.Velocity;
			var center = physics.Position + Vector3.Up * LegRadius;
			if ( velocity.z <= -600f || !FindLedge( ref center, velocity.z, out var wall, out float height, out var surface, out var contact ) ) return false;
			if ( SelectBodyImpact( velocity.Dot( wall ) / 40f ) is { } impact )
				PlayMaterialEvent( impact, contact.HitPosition, TerrainLoader.ContactSurface( contact, contact.HitPosition )?.ResourceName, 1f );
			climbDirection = wall;
			LedgeHeight = height;
			LedgeGrabOrigin = center;
			LedgeGrabTime = Time.Now;
			approachingLedge = false;
			LedgeAnimation = center.z < height - 40f ? LedgeReach : ClimbUp;
			LedgeMirrored = LedgeAnimation == LedgeReach && Random.Shared.Next( 2 ) == 0;
			LedgePhase = 0f;
			float target = height - 42f;
			climbPosition = center.WithZ( Math.Clamp( center.z, target - 4f, target + 20f ) - LegRadius );
			LedgeHeightOffset = target - climbPosition.z - LegRadius;
			PlayMaterialEvent( "edge_grab", center + wall * LegRadius, surface?.ResourceName, 1f );
			body.MotionEnabled = false;
			body.Velocity = Vector3.Zero;
			UseAirHull( false );
			MovementCapsule.Enabled = false;
			FaceInAir( Rotation.LookAt( wall ) );
		}

		// An AI always asks to pull up on its next ledge update.
		if ( LedgeAnimation == LedgeReach && LedgePhase > 0f )
		{
			LedgeAnimation = ClimbUp;
			LedgeMirrored = false;
			LedgePhase = 0f;
		}
		var clip = boundAnimations.Clips[LedgeAnimation].ForSide( LedgeMirrored );
		float previousPhase = LedgePhase;
		var from = clip.Animation.GetRootMotion( previousPhase );
		LedgePhase = Math.Min( 1f, LedgePhase + Time.Delta / clip.Duration );
		PlayCycleSteps( clip, previousPhase, LedgePhase );
		var to = clip.Animation.GetRootMotion( LedgePhase );
		float correction = LedgeHeightOffset * (1f - MathF.Pow( 0.98f, Time.Delta * 120f ));
		LedgeHeightOffset -= correction;
		float rise = LedgeAnimation == ClimbUp ? 4f * Time.Delta : 0f;
		climbPosition += Vector3.Up * (correction + rise) + WorldRotation * ((to.Position - from.Position) * WorldScale);
		WorldPosition = climbPosition;
		if ( LedgePhase >= 1f )
		{
			FinishClimb( body );
		}
		return true;
	}

	void FinishClimb( Rigidbody body )
	{
		var center = climbPosition + climbDirection * 5.2f + Vector3.Up * LegRadius;
		float depth = boundAnimations.Species == "wolf" ? 20f : 8f;
		var support = ClimbTrace( Scene.Trace.Sphere( LegRadius, center + Vector3.Up * 4f, center + Vector3.Down * depth ) ).Run();
		WorldPosition = (support.Hit && !support.StartedSolid ? support.EndPosition : center) - Vector3.Up * LegRadius;
		EndLedge();
		duckAmount = CrouchAmount = groundCrouchTarget = 1f;
		duckVelocity = 2f;
		body.Velocity = climbDirection * (0.2f * GroundRunSpeed);
		leavingLedge = support.Hit && Movement.MoveTarget.HasValue;
	}

	internal void EndLedge()
	{
		approachingLedge = leavingLedge = false;
		if ( LedgeAnimation is null ) return;
		LedgeAnimation = null;
		LedgePhase = 0f;
		if ( GetComponent<Rigidbody>() is { } body ) body.MotionEnabled = true;
		if ( MovementCapsule.IsValid() ) MovementCapsule.Enabled = true;
	}
}
