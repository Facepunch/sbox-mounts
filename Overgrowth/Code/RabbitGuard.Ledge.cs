using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string ClimbUp = "r_ledge_climb_fast", LedgeReach = "r_ledge_barely_reach";
	[Sync] string LedgeAnimation { get; set; }
	[Sync] bool LedgeMirrored { get; set; }
	[Sync( SyncFlags.Interpolate )] float LedgePhase { get; set; }
	Vector3 climbDirection, climbPosition;
	internal bool OnLedge => LedgeAnimation is not null;
	float jumpFuel, heightOffset;
	TimeSince sinceClimbLaunch;
	bool approachingLedge;
	const float LegRadius = 18f;

	SceneTrace ClimbTrace( SceneTrace trace ) => trace.IgnoreGameObjectHierarchy( GameObject ).IgnoreDynamic().WithoutTags( "npc", "player", "trigger" );

	internal bool TryClimbLaunch( Vector3 destination, out Vector3 velocity )
	{
		velocity = default;
		var direction = (destination - WorldPosition).WithZ( 0f ).Normal;
		if ( boundAnimations is null || direction.IsNearlyZero() || destination.z <= WorldPosition.z ) return false;
		var normal = GroundObject.IsValid() ? ClimbTrace( Scene.Trace.Ray( WorldPosition + Vector3.Up * 12f, WorldPosition + Vector3.Down * 12f ) ).Run().Normal : Vector3.Up;
		velocity = direction * GroundRunSpeed + Vector3.Up * 200f;
		if ( !normal.IsNearlyZero() && velocity.Normal.Dot( normal ) < 0.3f )
		{
			var right = normal.Cross( direction ).WithZ( 0f ).Normal;
			velocity = right.Cross( normal ).Normal * GroundRunSpeed + normal * 200f;
		}
		velocity.z = Math.Min( velocity.z, 200f ) * (boundAnimations.Species == "rabbit" ? 1f : 0.6f);
		return true;
	}

	internal void StartLedgeApproach( Vector3 launch )
	{
		approachingLedge = true;
		climbDirection = launch.WithZ( 0f ).Normal;
		sinceClimbLaunch = 0f;
		jumpFuel = boundAnimations.Species == "rabbit" ? 200f : 40f;
	}

	bool FindLedge( Vector3 center, float verticalSpeed, out Vector3 wall, out float height, out Surface surface )
	{
		wall = default;
		height = 0f;
		surface = null;
		for ( int i = 0; i < 3 && wall.IsNearlyZero(); i++ )
		{
			var origin = center + Vector3.Up * (12f * i);
			float best = float.PositiveInfinity;
			foreach ( var hit in ClimbTrace( Scene.Trace.Sphere( LegRadius * 1.5f, origin, origin ) ).RunAll() )
			{
				var offset = hit.HitPosition - origin;
				if ( offset.Normal.z < -0.7f || offset.WithZ( 0f ).IsNearlyZero() || offset.LengthSquared >= best ) continue;
				wall = offset.WithZ( 0f ).Normal;
				best = offset.LengthSquared;
			}
		}
		if ( wall.IsNearlyZero() ) return false;
		var over = center + wall * 20f;
		var down = ClimbTrace( Scene.Trace.Cylinder( 40f, LegRadius, over + Vector3.Up * 100f, over + Vector3.Up * 20f ) ).Run();
		if ( !down.Hit || down.StartedSolid ) return false;
		var clear = down.EndPosition + Vector3.Up * 2.8f;
		if ( ClimbTrace( Scene.Trace.Cylinder( 40f, LegRadius, clear, clear ) ).Run().Hit ) return false;
		height = down.EndPosition.z - 20f;
		surface = down.Surface;
		var rim = center.WithZ( height );
		var outside = rim - wall * 4f;
		if ( ClimbTrace( Scene.Trace.Sphere( LegRadius, outside, outside + Vector3.Down * 20f ) ).Run().Hit ) return false;
		if ( !ClimbTrace( Scene.Trace.Cylinder( 40f, LegRadius, rim, rim + wall * 40f ) ).Run().Hit ) return false;
		float reach = verticalSpeed >= 60f ? 40f : verticalSpeed > 0f ? 100f : 60f;
		return center.z > height - reach;
	}

	internal bool AdvanceLedge()
	{
		if ( !approachingLedge && LedgeAnimation is null ) return false;
		if ( Downed || PhysicsCarrier.IsValid() || Health < 1f ) { EndLedge(); return false; }
		var body = GetComponent<Rigidbody>();
		var physics = body?.PhysicsBody;
		if ( !physics.IsValid() ) { EndLedge(); return false; }
		if ( LedgeAnimation is null )
		{
			if ( sinceClimbLaunch > 2f || IsOnGround && sinceClimbLaunch > 0.1f ) { EndLedge(); return false; }
			var velocity = body.Velocity + climbDirection * (120f * Time.Delta);
			if ( jumpFuel > 0f && velocity.z > 0f )
			{
				float burn = 400f * Time.Delta;
				jumpFuel -= burn;
				velocity.z += burn;
			}
			body.Velocity = velocity;
			var center = physics.Position + Vector3.Up * LegRadius;
			if ( velocity.z <= -600f || !FindLedge( center, velocity.z, out var wall, out float height, out var surface ) ) return false;
			climbDirection = wall;
			approachingLedge = false;
			LedgeAnimation = center.z < height - 40f ? LedgeReach : ClimbUp;
			LedgeMirrored = LedgeAnimation == LedgeReach && Random.Shared.Next( 2 ) == 0;
			LedgePhase = 0f;
			float target = height - 42f;
			climbPosition = physics.Position.WithZ( Math.Clamp( center.z, target - 4f, target + 20f ) - LegRadius );
			heightOffset = target - climbPosition.z - LegRadius;
			PlayMaterialEvent( "edge_grab", center + wall * LegRadius, surface?.ResourceName, 1f );
			body.MotionEnabled = false;
			body.Velocity = Vector3.Zero;
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
		var from = clip.Animation.GetRootMotion( LedgePhase );
		LedgePhase = Math.Min( 1f, LedgePhase + Time.Delta / clip.Duration );
		var to = clip.Animation.GetRootMotion( LedgePhase );
		float correction = heightOffset * (1f - MathF.Pow( 0.98f, Time.Delta * 120f ));
		heightOffset -= correction;
		climbPosition += Vector3.Up * correction + WorldRotation * ((to.Position - from.Position) * WorldScale);
		WorldPosition = climbPosition;
		if ( LedgePhase >= 1f )
		{
			WorldPosition = climbPosition + climbDirection * 5.2f;
			EndLedge();
		}
		return true;
	}

	internal void EndLedge()
	{
		approachingLedge = false;
		if ( LedgeAnimation is null ) return;
		LedgeAnimation = null;
		LedgePhase = 0f;
		if ( GetComponent<Rigidbody>() is { } body ) body.MotionEnabled = true;
		if ( MovementCapsule.IsValid() ) MovementCapsule.Enabled = true;
	}
}
