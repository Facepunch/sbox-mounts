using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	Vector3 runGroundNormal;

	// WorldToGroundSpace keeps an AI character's requested horizontal heading on slopes.
	static Vector3 GroundDirection( Vector3 direction, Vector3 normal )
	{
		var left = normal.Cross( Vector3.Forward ).Normal;
		var tangent = left * direction.y + left.Cross( normal ) * direction.x;
		return (direction * tangent.WithZ( 0f ).Length).WithZ( tangent.z );
	}

	Vector3 FloorNormal()
	{
		var floor = Scene.Trace.Ray( WorldPosition + Vector3.Up * 12f, WorldPosition + Vector3.Down * 32f )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		return floor.Hit && !floor.StartedSolid ? floor.Normal : Vector3.Up;
	}

	// UpdateGroundMovementControls adds acceleration before ApplyPhysics damps the body.
	// Give the crowd the speed reachable in its next step so it can still steer around actors and walls.
	internal void UpdateGroundSpeed( NavMeshAgent agent, float requested, float step )
	{
		if ( step <= 0f ) return;
		const float tick = 4f / 120f, acceleration = 35f * 40f;
		var velocity = agent.Velocity.WithZ( 0f );
		float speed = velocity.Length;
		float run = GroundRunSpeed;
		var direction = (agent.GetLookAhead( 40f ) - agent.AgentPosition).WithZ( 0f ).Normal;
		var ground = FloorNormal();
		runGroundNormal = runGroundNormal.IsNearlyZero() || speed < 0.01f ? ground
			: Vector3.Lerp( ground, runGroundNormal, MathF.Pow( 0.9f, step / tick ) ).Normal;
		var tangent = GroundDirection( direction, runGroundNormal );
		float input = requested / run;
		float slopeLimit = run * (1f - 0.7f * Math.Clamp( tangent.z * input, -1f, 1f ));
		float limit = Math.Min( run * 1.5f, Math.Max( agent.Velocity.Length * 0.98f, slopeLimit ) );
		bool wolf = boundAnimations?.Species == "wolf";
		if ( wolf ) limit *= 1.5f;
		float friction = acceleration / limit;
		float crouch = 1f - duckAmount * (boundAnimations?.CharacterSpecies == "cat" ? 0.25f : 0.5f);
		float drive = acceleration * crouch * PlantMovementScale * (wolf ? 0.8f : 1f);
		float terminal = input * MathF.Sqrt( Math.Max( 0f, 1f - tangent.z * tangent.z ) ) * drive * tick / (MathF.Exp( friction * tick ) - 1f);
		float response = 1f - MathF.Exp( -friction * step );
		agent.MaxSpeed = speed + (terminal - speed) * response;
		// Reversing input both brakes the old motion and accelerates toward the new route.
		agent.Acceleration = Math.Max( acceleration, (direction * terminal - velocity).Length * (response / step) );
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
		AdvanceMomentum( ref velocity, step, MathF.Exp( -(35f * 40f) / (30f * limit) ), origin );
	}

	bool MoveTowardPoint( Vector3 target, float slowRadius, float speed )
	{
		var intent = MovementToPoint( target, slowRadius, 0f, 0f, out var point );
		return MoveWithIntent( point, intent, speed );
	}

	bool MoveWithIntent( Vector3 point, Vector3 intent, float speed )
	{
		if ( intent.IsNearlyZero() )
		{
			ReleaseMovementInput();
			return false;
		}
		Movement.MoveTo( point, 0f, intent.Length * speed * GroundRunSpeed );
		return true;
	}

	Vector3 MovementToPoint( Vector3 target, float slowRadius, float preferredRange, float strafeOffset, out Vector3 point )
	{
		var toward = (target - WorldPosition).WithZ( 0f ).Normal;
		point = Movement.ProjectPoint( target ) ?? target;
		var corner = NextPathCorner( point, out _ );
		if ( corner.DistanceSquared( target ) < preferredRange * preferredRange )
		{
			var offset = -toward * preferredRange + Vector3.Up.Cross( toward ) * strafeOffset;
			point = WalkableRay( point, point + offset );
			corner = NextPathCorner( point, out _ );
		}
		return PathSteering( corner, toward, slowRadius, ref point );
	}

	Vector3 PathSteering( Vector3 corner, Vector3 toward, float slowRadius, ref Vector3 point )
	{
		var delta = (corner - WorldPosition).WithZ( 0f );
		var direct = toward * delta.Dot( toward );
		var intent = (delta - direct + direct.Normal * Math.Max( 0f, direct.Length - slowRadius )) / 40f;
		var separation = TeammateSeparation();
		intent += separation;
		if ( intent.LengthSquared > 1f ) intent = intent.Normal;
		if ( !separation.IsNearlyZero() ) point = WorldPosition + intent * 40f;
		return intent;
	}

	Vector3 NextPathCorner( Vector3 goal, out Vector3? end )
	{
		var path = Scene.NavMesh.CalculatePath( new() { Start = WorldPosition, Target = goal, Agent = GetComponent<NavMeshAgent>() } );
		end = path.IsValid && path.Points.Count > 0 ? path.Points[^1].Position : null;
		if ( !end.HasValue || path.Points.Count < 2 ) return end ?? goal;
		for ( int i = 1; i < path.Points.Count - 1; i++ )
		{
			var corner = path.Points[i].Position;
			if ( (corner - WorldPosition).WithZ( 0f ).LengthSquared > 40f * 40f ) return corner;
		}
		return path.Points[^1].Position;
	}

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
			// Recover the boundary normal just inside the polygon before projecting the
			// remainder of the ray, as NavMesh::RayCastSlide does at each wall.
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
