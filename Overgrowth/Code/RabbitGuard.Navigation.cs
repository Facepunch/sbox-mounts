using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	bool MoveTowardPoint( Vector3 target, float slowRadius, float speed )
	{
		var intent = MovementToPoint( target, slowRadius, 0f, 0f, out var point );
		if ( intent.IsNearlyZero() )
		{
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			return false;
		}
		Movement.MoveTo( point, 0f, intent.Length * speed * GroundRunSpeed );
		return true;
	}

	bool ApproachInvestigation( Vector3 target, float slowRadius, float speed ) =>
		moveDelay > 0f ? MoveTowardPoint( WorldPosition, 0f, speed ) : MoveTowardPoint( target, slowRadius, speed );

	Vector3 MovementToPoint( Vector3 target, float slowRadius, float preferredRange, float strafeOffset, out Vector3 point )
	{
		var toward = (target - WorldPosition).WithZ( 0f ).Normal;
		point = Movement.ProjectPoint( target ) ?? target;
		var corner = NextPathCorner( point );
		if ( corner.DistanceSquared( target ) < preferredRange * preferredRange )
		{
			var offset = -toward * preferredRange + Vector3.Up.Cross( toward ) * strafeOffset;
			point = WalkableRay( point, point + offset );
			corner = NextPathCorner( point );
		}
		var delta = (corner - WorldPosition).WithZ( 0f );
		var direct = toward * delta.Dot( toward );
		var intent = (delta - direct + direct.Normal * Math.Max( 0f, direct.Length - slowRadius )) / 40f;
		var separation = TeammateSeparation();
		intent += separation;
		if ( intent.LengthSquared > 1f ) intent = intent.Normal;
		if ( !separation.IsNearlyZero() ) point = WorldPosition + intent * 40f;
		return intent;
	}

	Vector3 NextPathCorner( Vector3 goal )
	{
		var path = Scene.NavMesh.CalculatePath( new() { Start = WorldPosition, Target = goal, Agent = GetComponent<NavMeshAgent>() } );
		if ( !path.IsValid || path.Points.Count < 2 ) return goal;
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
