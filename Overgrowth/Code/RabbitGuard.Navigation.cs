using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	const float GroundAcceleration = 35f * 40f;
	Vector3 runGroundNormal;
	float PreFrictionSpeedScale { get; set; } = 1f;

	[Sync] float GroundSpeedLimit
	{
		get;
		set
		{
			field = value;
			PreFrictionSpeedScale = value > 0f ? MathF.Exp( GroundAcceleration / (30f * value) ) : 1f;
		}
	}

	float GroundDrive( float duck )
	{
		float crouch = 1f - duck * (boundAnimations?.CharacterSpecies == "cat" ? 0.25f : 0.5f);
		return GroundAcceleration * crouch * PlantMovementScale( duck ) * (boundAnimations?.Species == "wolf" ? 0.8f : 1f);
	}

	float MovementSpeedRatio( Vector3 velocity ) => velocity.Length / (GroundSpeedLimit > 0f ? GroundSpeedLimit : GroundRunSpeed);

	static Vector3 GroundDirection( Vector3 direction, Vector3 normal )
	{
		var left = normal.Cross( Vector3.Forward ).Normal;
		var tangent = left * direction.y + left.Cross( normal ) * direction.x + normal * direction.z;
		return (direction * tangent.WithZ( 0f ).Length).WithZ( tangent.z );
	}

	Vector3 FloorNormal()
	{
		var floor = Scene.Trace.Ray( WorldPosition + Vector3.Up * 12f, WorldPosition + Vector3.Down * 32f )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		return floor.Hit && !floor.StartedSolid ? floor.Normal : Vector3.Up;
	}

	internal Vector3? StandingPosition( Vector3 position )
	{
		if ( boundAnimations is null || !IsOnGround || Downed || Health <= 0f || PhysicsCarrier.IsValid() || RollProgress < 1f ) return null;
		var (start, end) = StandingSweep( position );
		var ground = Scene.Trace.Sphere( LegRadius, start, end ).IgnoreGameObjectHierarchy( GameObject )
			.IgnoreDynamic().WithoutTags( "npc", "trigger" ).Run();
		return ground.Hit && !ground.StartedSolid && ground.Normal.z > 0.5f
			? position.WithZ( ground.EndPosition.z - LegRadius ) : null;
	}

	internal void UpdateGroundSpeed( NavMeshAgent agent, float requested, float step, Vector3? requestedHeading )
	{
		if ( step <= 0f ) return;
		const float tick = 4f / 120f, acceleration = GroundAcceleration;
		var velocity = agent.Velocity.WithZ( 0f );
		float speed = velocity.Length;
		float run = GroundRunSpeed;
		// A new AI request can precede the crowd's next corridor update.
		var direction = requestedHeading ?? (agent.GetLookAhead( 40f ) - agent.AgentPosition).WithZ( 0f ).Normal;
		var ground = FloorNormal();
		runGroundNormal = runGroundNormal.IsNearlyZero() || speed < 0.01f ? ground
			: Vector3.Lerp( ground, runGroundNormal, MathF.Pow( 0.9f, step / tick ) ).Normal;
		var tangent = GroundDirection( direction, runGroundNormal );
		float input = requested / run;
		float slopeLimit = run * (1f - 0.7f * Math.Clamp( tangent.z * input, -1f, 1f ));
		float limit = Math.Min( run * 1.5f, Math.Max( agent.Velocity.Length * 0.98f, slopeLimit ) );
		bool wolf = boundAnimations?.Species == "wolf";
		if ( wolf ) limit *= 1.5f;
		GroundSpeedLimit = limit;
		float friction = acceleration / limit;
		// The crowd steps before the displayed pose. Sample the spring at the motor's
		// simulation time without advancing the pose a second time.
		float elapsed = (float)(Time.NowDouble - groundCrouchTime);
		float duck = groundCrouchTime > 0 && elapsed > 0f && InLocomotion ? SampleCrouchSpring( groundCrouchTarget, elapsed ).Amount : duckAmount;
		float drive = GroundDrive( duck );
		float terminal = input * tangent.WithZ( 0f ).Length * drive * tick / (PreFrictionSpeedScale - 1f);
		float response = 1f - MathF.Exp( -friction * step );
		float steeringAcceleration = (direction.WithZ( 0f ).Normal * terminal - velocity).Length * (response / step);
		// The crowd limits its wish speed by braking distance. While there is room,
		// approach the authored velocity vector, preserving momentum through turns.
		float distance = agent.TargetPosition is { } target ? (target - agent.AgentPosition).WithZ( 0f ).Length : 0f;
		if ( terminal * terminal <= 2f * steeringAcceleration * distance )
		{
			agent.MaxSpeed = terminal;
			agent.Acceleration = steeringAcceleration;
		}
		else
		{
			agent.MaxSpeed = speed + (terminal - speed) * response;
			agent.Acceleration = Math.Max( acceleration, steeringAcceleration );
		}
	}

	void ReleaseMovementInput()
	{
		if ( Movement is CharacterNavigation navigation ) navigation.ReleaseInput();
		else Movement.Stop();
	}

	internal void AdvanceBraking( ref Vector3 velocity, float step, Vector3 origin )
	{
		float limit = Math.Clamp( velocity.Length * 0.98f, GroundRunSpeed, GroundRunSpeed * 1.5f );
		if ( boundAnimations?.Species == "wolf" ) limit *= 1.5f;
		GroundSpeedLimit = limit;
		AdvanceMomentum( ref velocity, step, 1f / PreFrictionSpeedScale, origin );
	}

	bool MoveTowardPoint( Vector3 target, float slowRadius, float speed )
	{
		var intent = MovementToPoint( target, slowRadius, 0f, 0f, out var point );
		return MoveWithIntent( point, intent, speed );
	}

	bool MoveWithIntent( Vector3 point, Vector3 intent, float speed, GameObject faceTarget = null )
	{
		if ( intent.IsNearlyZero() )
		{
			ReleaseMovementInput();
			return false;
		}
		Movement.MoveTo( point, 0f, intent.Length * speed * GroundRunSpeed, faceTarget );
		if ( Movement is CharacterNavigation navigation ) navigation.RequestedHeading = intent.Normal;
		return true;
	}

	Vector3 MovementToPoint( Vector3 target, float slowRadius, float preferredRange, float strafeOffset, out Vector3 point, Vector3? separation = null )
	{
		var toward = (target - WorldPosition).WithZ( 0f ).Normal;
		point = target.DistanceSquared( WorldPosition ) > 0.2f * 40f * 40f ? Movement.ProjectPoint( target ) ?? target : target;
		var next = NextPathCorner( point, out _ );
		if ( !next.HasValue && Movement is CharacterNavigation { StickToNavMesh: true }
			&& Movement.ProjectPoint( WorldPosition ) is { } surface && surface != WorldPosition )
			return Vector3.Zero;

		var corner = next ?? point;
		if ( corner.DistanceSquared( target ) < preferredRange * preferredRange )
		{
			var offset = -toward * preferredRange + Vector3.Up.Cross( toward ) * strafeOffset;
			point = WalkableRay( target, target + offset );
			corner = NextPathCorner( point, out _ ) ?? point;
		}
		return PathSteering( corner, toward, slowRadius, ref point, separation );
	}

	Vector3 PathSteering( Vector3 corner, Vector3 toward, float slowRadius, ref Vector3 point, Vector3? repulsion = null )
	{
		var delta = (corner - WorldPosition).WithZ( 0f );
		var direct = toward * delta.Dot( toward );
		var intent = (delta - direct + direct.Normal * Math.Max( 0f, direct.Length - slowRadius )) / 40f;
		var separation = repulsion ?? TeammateSeparation();
		intent += separation;
		if ( intent.LengthSquared > 1f ) intent = intent.Normal;
		if ( !separation.IsNearlyZero() ) point = WorldPosition + intent.WithZ( 0f ) * 40f;
		return intent;
	}

	Vector3? NextPathCorner( Vector3 goal, out Vector3? end )
	{
		var path = Scene.NavMesh.CalculatePath( new() { Start = WorldPosition, Target = goal, Agent = GetComponent<NavMeshAgent>() } );
		end = path.IsValid && path.Points.Count > 0 ? path.Points[^1].Position : null;
		if ( !end.HasValue || path.Points.Count < 2 ) return null;
		for ( int i = 1; i < path.Points.Count - 1; i++ )
		{
			var corner = path.Points[i].Position;
			if ( (corner - WorldPosition).WithZ( 0f ).LengthSquared > 40f * 40f ) return corner;
		}
		return path.Points[^1].Position;
	}

	Vector3? NavPoint( Vector3 position ) => Scene.NavMesh.GetClosestPoint(
		BBox.FromPositionAndSize( position, new Vector3( 4f, 4f, 8f ) * 40f ) );

	Vector3 SlideOnNavMesh( Vector3 start, Vector3 end )
	{
		var nav = Scene.NavMesh;
		for ( int depth = 0; ; depth++ )
		{
			var hit = WalkableRay( start, end );
			var remaining = (end - hit).WithZ( 0f );
			if ( remaining.IsNearlyZero() || depth == 4 ) return hit;
			var approach = (hit - start).WithZ( 0f );
			if ( approach.IsNearlyZero() ) return hit;
			var inside = hit - approach.Normal * Math.Min( approach.Length, 1f );
			if ( nav.GetClosestEdge( inside, 4f ) is not { } edge ) return hit;
			var normal = (inside - edge).WithZ( 0f ).Normal;
			if ( normal.IsNearlyZero() ) return hit;
			end = hit + remaining - normal * remaining.Dot( normal );
			start = hit;
		}
	}

	// s&box exposes path corners and boundary points. A straight, connected ground
	// corridor reaches the requested horizontal endpoint without a turn or detour.
	Vector3 WalkableRay( Vector3 from, Vector3 to )
	{
		var delta = (to - from).WithZ( 0f );
		float length = delta.Length;
		var nav = Scene.NavMesh;
		if ( length <= 0.01f ) return from;
		if ( length <= 40f && nav.GetClosestEdge( from, length ) is null &&
			nav.GetClosestPoint( from + delta, 40f ) is { } surface &&
			(surface - to).WithZ( 0f ).LengthSquared < 0.0001f ) return surface;

		bool Reach( Vector3 start, Vector3 target, out Vector3 reached )
		{
			reached = start;
			var path = nav.CalculatePath( new() { Start = start, Target = target } );
			if ( !path.IsValid || path.Points.Count < 1 ) return false;
			reached = path.Points[^1].Position;
			if ( (reached - target).WithZ( 0f ).LengthSquared > 0.0001f ) return false;
			var direction = delta / length;
			for ( int i = 1; i < path.Points.Count - 1; i++ )
			{
				var offset = (path.Points[i].Position - from).WithZ( 0f );
				if ( (offset - direction * direction.Dot( offset )).LengthSquared > 0.0001f ) return false;
			}
			return true;
		}
		// A long ray can cross a slope outside the goal search's vertical snap range.
		// Continue each successful subsegment from its surface height before testing the rest.
		Vector3 Advance( Vector3 start, Vector3 target )
		{
			if ( Reach( start, target.WithZ( start.z ), out var end ) ) return end;
			var step = (target - start).WithZ( 0f );
			if ( step.Length <= 0.05f ) return start;
			var mid = start + step * 0.5f;
			var reached = Advance( start, mid );
			if ( (reached - mid).WithZ( 0f ).LengthSquared > 0.0001f ) return reached;
			return Advance( reached, target );
		}
		return Advance( from, to );
	}
}
