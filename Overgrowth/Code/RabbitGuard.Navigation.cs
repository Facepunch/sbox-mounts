using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
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
