using Sandbox.Navigation;
using System;

namespace NS2;

public sealed partial class MapLocations
{
	readonly record struct GatewayRoute( Vector3 Entry, Vector3 Exit, float Length );
	readonly Dictionary<string, Vector3[]> roomGateways = [];
	readonly Dictionary<(string, string), GatewayRoute?> walkRoutes = [];

	internal float WalkDistance( Vector3 from, Vector3 to )
	{
		var start = LocationAt( from );
		var end = LocationAt( to );
		if ( start is null || end is null || start == end || !NavigationReady() )
			return from.Distance( to );

		if ( !walkRoutes.TryGetValue( (start, end), out var route ) )
		{
			var entries = Gateways( start );
			var exits = Gateways( end );
			if ( entries is null || exits is null ) return from.Distance( to );

			foreach ( var entry in entries )
			foreach ( var exit in exits )
			{
				float length = GatewayDistance( entry, exit );
				if ( length < (route?.Length ?? float.PositiveInfinity) )
					route = new( entry, exit, length );
			}

			walkRoutes[(start, end)] = route;
			walkRoutes[(end, start)] = route is { } r ? new( r.Exit, r.Entry, r.Length ) : null;
		}

		return route is { } path ? from.Distance( path.Entry ) + path.Length + to.Distance( path.Exit ) : from.Distance( to );
	}

	float GatewayDistance( Vector3 from, Vector3 to )
	{
		if ( from == to ) return 0;
		var path = Scene.NavMesh.CalculatePath( new() { Start = from, Target = to } );
		if ( path.Status != NavMeshPathStatus.Complete ) return float.PositiveInfinity;
		float length = 0;
		for ( int i = 1; i < path.Points.Count; i++ )
			length += path.Points[i - 1].Position.Distance( path.Points[i].Position );
		return length;
	}

	Vector3[] Gateways( string room )
	{
		if ( roomGateways.TryGetValue( room, out var gates ) ) return gates;
		int index = Array.FindIndex( Destinations, d => d.Name == room );
		if ( index < 0 ) return [];
		var neighbors = Neighbors( room );
		if ( readingGrid ) return null;

		List<Vector3> points = [];
		if ( Project( Destinations[index] ) is { } start )
		{
			foreach ( var neighbor in neighbors )
			{
				// Spark keeps the first path between two destinations in map order.
				var point = index < Array.FindIndex( Destinations, d => d.Name == neighbor.Name )
					? FindGateway( room, start, neighbor.Name, neighbor.Position )
					: FindGateway( neighbor.Name, neighbor.Position, room, start );
				if ( point is { } p ) points.Add( p );
			}
		}
		return roomGateways[room] = points.ToArray();
	}

	Vector3? FindGateway( string a, Vector3 from, string b, Vector3 to )
	{
		var path = Scene.NavMesh.CalculatePath( new() { Start = from, Target = to } );
		if ( path.Status != NavMeshPathStatus.Complete ) return null;
		Vector3? last = null, first = null;
		foreach ( var point in path.Points )
		{
			var room = LocationAt( point.Position );
			if ( room == a ) last = point.Position;
			else if ( room == b ) first ??= point.Position;
		}
		if ( last is not { } left || first is not { } right ) return null;

		// Spark bisects the two path points until the boundary is within half a metre.
		for ( int i = 0; i < 14 && left.Distance( right ) > 0.5f * MapLoader.Scale; i++ )
		{
			var mid = (left + right) * 0.5f;
			var room = LocationAt( mid );
			if ( room == a ) left = mid;
			else if ( room == b ) right = mid;
			else break;
		}
		return (left + right) * 0.5f;
	}
}
