using Sandbox.Navigation;
using System;

namespace NS2;

public sealed partial class Fade
{
	NavMeshPath travelPath;
	TimeUntil refreshTravelPath;
	Vector3 pathGoal;
	int nextCorner;

	void UpdateTravelPath( Vector3 goal )
	{
		if ( refreshTravelPath > 0 && pathGoal.DistanceSquared( goal ) <= Scale * Scale ) return;
		pathGoal = goal;
		refreshTravelPath = 0.2f;
		var agent = GetComponent<NavMeshAgent>();
		travelPath = agent is { IsNavigating: true } ? agent.GetPath() : default;
		if ( travelPath.Points.Count == 0 && Scene.NavMesh.IsEnabled )
			travelPath = Scene.NavMesh.CalculatePath( new() { Start = WorldPosition, Target = goal } );
		nextCorner = 1;
		CheckEntrance( travelPath.Points );
	}

	// BotMotion averages the ground and the next four metres of Spark's lifted path points.
	static float TravelHeight( Vector3 ground, IReadOnlyList<NavMeshPathPoint> corners, int next )
	{
		float sum = ground.z;
		int count = 1;
		var from = ground;
		var last = ground;
		float traveled = 0;
		for ( int i = next; i < corners.Count; i++ )
		{
			var end = corners[i].Position;
			float length = from.Distance( end );
			for ( float distance = 0; distance < length; )
			{
				distance = Math.Min( length, distance + 0.5f * Scale );
				var sample = Vector3.Lerp( from, end, distance / length ) + Vector3.Up * (0.9f * Scale);
				sum += sample.z;
				count++;
				traveled += last.Distance( sample );
				if ( traveled >= 4 * Scale ) return sum / count;
				last = sample;
			}
			from = end;
		}
		return sum / count;
	}

	static Vector3 TravelLookDirection( Rotation current, Vector3 direction, float rise, float run )
	{
		float pitch = -MathF.Atan2( rise, run ).RadianToDegree();
		float eased = AlienAim.ApproachAngle( current.Angles().pitch, pitch, 0.8f );
		return direction.WithZ( 0 ).Normal.WithZ( -MathF.Sin( eased.DegreeToRadian() ) );
	}

	Vector3 TravelLook( Vector3 goal )
	{
		var points = travelPath.Points;
		while ( nextCorner < points.Count && (points[nextCorner].Position - WorldPosition).WithZ( 0 ).LengthSquared < 0.25f * Scale * Scale ) nextCorner++;
		var direction = (nextCorner < points.Count ? points[nextCorner].Position : goal) - WorldPosition;
		if ( direction.WithZ( 0 ).IsNearlyZero() ) return view.Forward;

		float desiredHeight = WorldPosition.z;
		if ( InCombat && goal.DistanceSquared( WorldPosition ) < 8.5f * 8.5f * Scale * Scale ) desiredHeight = goal.z;
		else if ( Scene.NavMesh.IsEnabled && Scene.NavMesh.GetClosestPoint( WorldPosition ) is { } ground )
			desiredHeight = TravelHeight( ground, points, nextCorner ) + 1.3f * Scale;

		var velocity = IsOnGround ? Animation.MoveVelocity : GetComponent<Rigidbody>().Velocity;
		var ahead = velocity.LengthSquared > Scale * Scale ? velocity.Normal : direction.WithZ( 0 ).Normal;
		var trace = Scene.Trace.Ray( EyePosition, EyePosition + ahead * (4 * Scale) ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "player", "playercontroller", "ns2_lifeform", "ns2_marine", "ns2_exo", "ragdoll" ).Run();
		float run = trace.Hit ? (trace.EndPosition - WorldPosition).WithZ( 0 ).Length : 4 * Scale;
		return TravelLookDirection( view, direction, desiredHeight - WorldPosition.z, run );
	}
}
