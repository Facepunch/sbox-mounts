using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property, Group( "Fear" )] public bool CausesFearOnSight { get; set; }
	[Property, Group( "Fear" )] public bool AlwaysAfraidOnSight { get; set; }
	[Property, Group( "Fear" )] public bool NeverAfraidOnSight { get; set; }
	[Property, Group( "Fear" ), Range( 0f, 1f )] public float AfraidAtHealthLevel { get; set; }

	GameObject fleeFrom;
	TimeUntil fleeUpdate;

	bool HasCombatTarget => !Fleeing && CanAttack( enemy );
	bool Fleeing => fleeFrom.IsValid() && fleeFrom == enemy;

	bool Flee()
	{
		if ( !CanAttack( enemy ) ) { fleeFrom = null; return false; }
		if ( !Fleeing && Health <= MaxHealth * AfraidAtHealthLevel ) fleeFrom = enemy;
		if ( !Fleeing ) return false;
		if ( startled > 0f )
		{
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			Stance = Ready;
			return true;
		}
		if ( fleeUpdate <= 0f )
		{
			fleeUpdate = Random.Shared.Float( 0.2f, 0.5f );
			var away = (WorldPosition - enemy.WorldPosition).WithZ( 0f ).Normal;
			if ( !away.IsNearlyZero() && RetreatDestination( away ) is { } point )
				Movement.MoveTo( point, 0f, GroundRunSpeed );
			else Movement.Stop();
		}
		Stance = Locomotion;
		return true;
	}

	Vector3? RetreatDestination( Vector3 away )
	{
		// NavRaycastSlide projects the remaining ray along each obstruction. The mount
		// has world sweeps and path queries, so constrain the result to a connected corridor.
		var center = WorldPosition + Vector3.Up * 24f;
		var end = center + away * 40000f;
		for ( int i = 0; i <= 4; i++ )
		{
			var hit = ClimbTrace( Scene.Trace.Sphere( 18f, center, end ) ).Run();
			if ( !hit.Hit ) { center = end; break; }
			if ( hit.StartedSolid ) break;
			center = hit.EndPosition + hit.Normal;
			var normal = hit.Normal.WithZ( 0f ).Normal;
			if ( normal.IsNearlyZero() ) break;
			end -= normal * normal.Dot( end - center );
			if ( center.DistanceSquared( end ) < 1f ) break;
		}
		var nav = Scene.NavMesh;
		var point = nav.GetClosestPoint( center ) ?? nav.GetClosestPoint( WorldPosition + away * GroundRunSpeed );
		if ( point is null ) return null;
		var path = nav.CalculatePath( new() { Start = WorldPosition, Target = point.Value, Agent = GetComponent<NavMeshAgent>() } );
		if ( !path.IsValid || path.Points.Count < 2 ) return null;
		var corner = path.Points[1].Position;
		return (corner - WorldPosition).Dot( away ) > 0f ? corner : null;
	}
}
