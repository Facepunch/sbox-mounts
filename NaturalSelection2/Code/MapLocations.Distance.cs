using Sandbox.Navigation;
using System;

namespace NS2;

public sealed partial class MapLocations
{
	readonly record struct GatewayRoute( Vector3 Entry, Vector3 Exit, float Length );
	readonly Dictionary<string, Vector3[]> roomGateways = [];
	readonly Dictionary<(string, string), GatewayRoute?> walkRoutes = [];

	internal float WalkDistance( Vector3 from, Vector3 to ) => WalkDistanceFromRoom( null, from, to );

	internal float WalkDistanceFromRoom( string start, Vector3 from, Vector3 to )
	{
		start ??= LocationAt( from );
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

	Vector3? FindGateway( string a, Vector3 from, string b, Vector3 to ) =>
		TraceGateway( a, from, b, to ) ?? TraceGateway( b, to, a, from );

	Vector3? TraceGateway( string a, Vector3 from, string b, Vector3 to )
	{
		var path = Scene.NavMesh.CalculatePath( new() { Start = from, Target = to } );
		if ( path.Status != NavMeshPathStatus.Complete || path.Points.Count < 2 ) return null;
		// Spark samples the corridor every half metre, 0.9 metres above its floor.
		// The last point of a complete path is the projected destination on the floor.
		const float spacing = 0.5f * MapLoader.Scale;
		var offset = Vector3.Up * (0.9f * MapLoader.Scale);
		float next = spacing;
		Vector3? lastSource = null, firstDestination = null;
		for ( int i = 1; i < path.Points.Count; i++ )
		{
			var start = path.Points[i - 1].Position;
			var delta = path.Points[i].Position - start;
			float length = delta.Length;
			for ( ; next < length; next += spacing )
			{
				var sample = start + delta * (next / length) + offset;
				var room = LocationAt( sample );
				if ( room == a ) lastSource = sample;
				else if ( lastSource is null ) return null;
				else if ( room == b ) firstDestination ??= sample;
				else if ( room is not null ) return null;
			}
			next -= length;
		}
		var end = path.Points[^1].Position;
		if ( lastSource is not { } left || LocationAt( end ) != b ) return null;
		var right = firstDestination ?? end;

		// Spark uses the original span to decide whether to take the 14 refinement steps.
		int steps = left.DistanceSquared( right ) > 0.25f * MapLoader.Scale * MapLoader.Scale ? 14 : 0;
		for ( int i = 0; i < steps; i++ )
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
