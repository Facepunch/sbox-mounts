using Sandbox.Navigation;

namespace NS2;

public sealed partial class Fade
{
	NavMeshLink entrance;
	Vector3 mouth, inward;
	float? entranceStarted;
	TimeUntil checkEntrance;

	NavMeshLink FindEntrance( IReadOnlyList<NavMeshPathPoint> path, out Vector3 top, out Vector3 direction )
	{
		top = direction = default;
		for ( int i = 1; i < path.Count; i++ )
		{
			var floor = path[i - 1].Position;
			var end = path[i].Position;
			var run = (end - floor).WithZ( 0 );
			if ( end.z <= floor.z || run.LengthSquared <= 0.09f * Scale * Scale ||
				(end - WorldPosition).WithZ( 0 ).LengthSquared >= 36 * Scale * Scale || !locations.IsVent( end ) ) continue;

			// Path corners contain the snapped link endpoints, including the direction the route crosses them.
			foreach ( var link in Scene.GetAllComponents<NavMeshLink>() )
			{
				var a = link.WorldStartPositionOnNavmesh;
				var b = link.WorldEndPositionOnNavmesh;
				if ( !(a == floor && b == end || link.IsBiDirectional && b == floor && a == end) ) continue;
				top = end;
				direction = run.Normal;
				return link;
			}
		}
		return null;
	}

	void CheckEntrance( Vector3 goal )
	{
		if ( !locations.IsValid() || !locations.HasVents || InCombat || locations.IsVent( WorldPosition ) )
		{
			entrance = null;
			entranceStarted = null;
			return;
		}
		if ( checkEntrance > 0 || !Scene.NavMesh.IsEnabled ) return;
		checkEntrance = 0.2f;
		var agent = GetComponent<NavMeshAgent>();
		var path = agent is { IsNavigating: true } ? agent.GetPath() : default;
		if ( path.Points.Count == 0 ) path = Scene.NavMesh.CalculatePath( new() { Start = WorldPosition, Target = goal } );
		var next = FindEntrance( path.Points, out mouth, out inward );
		if ( next != entrance ) entranceStarted = null;
		entrance = next;
	}

	bool EntranceAim( out Vector3? aim )
	{
		aim = null;
		if ( !entrance.IsValid() || InCombat || !locations.IsValid() || locations.IsVent( WorldPosition ) )
		{
			entranceStarted = null;
			return false;
		}
		var offset = mouth - WorldPosition;
		if ( offset.WithZ( 0 ).LengthSquared >= 36 * Scale * Scale )
		{
			entranceStarted = null;
			return false;
		}
		if ( offset.z <= Scale && !entranceStarted.HasValue ) return false;
		if ( !entranceStarted.HasValue )
		{
			if ( energy < 40 ) return true;
			entranceStarted = Time.Now;
		}
		float age = Time.Now - entranceStarted.Value;
		if ( age > 8 ) entranceStarted = Time.Now;
		else if ( age > 3 ) return true;

		var lift = Vector3.Up * (0.1f * Scale);
		var inside = mouth + inward * (0.8f * Scale) + lift;
		var ray = Scene.Trace.Ray( WorldPosition + lift, inside ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "player", "playercontroller", "ns2_lifeform", "ns2_marine", "ns2_exo" ).Run();
		aim = ray.Fraction > 0.95f ? inside : mouth - inward * Scale + Vector3.Up * (0.8f * Scale);
		return true;
	}
}
