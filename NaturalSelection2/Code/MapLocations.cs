using Sandbox.Navigation;

namespace NS2;

/// <summary>The named volumes and exploration destinations authored in a Spark level.</summary>
public sealed class MapLocations : Component
{
	public readonly record struct Volume( string Name, Transform Bounds );
	public readonly record struct Destination( string Name, Vector3 Position, bool Strategic );
	[Property] public Volume[] Volumes { get; set; } = [];
	[Property] public Destination[] Destinations { get; set; } = [];

	readonly Dictionary<string, Destination[]> connections = new();
	readonly Dictionary<string, Dictionary<string, int>> depths = new();

	internal IReadOnlyDictionary<string, int> DepthsFrom( string start )
	{
		if ( depths.TryGetValue( start, out var distances ) ) return distances;
		distances = new() { [start] = 0 };
		var queue = new Queue<string>();
		queue.Enqueue( start );
		while ( queue.TryDequeue( out var room ) )
		{
			foreach ( var neighbor in Neighbors( room ) )
				if ( distances.TryAdd( neighbor.Name, distances[room] + 1 ) ) queue.Enqueue( neighbor.Name );
		}
		return depths[start] = distances;
	}

	internal string LocationAt( Vector3 position )
	{
		foreach ( var volume in Volumes )
		{
			var p = volume.Bounds.PointToLocal( position );
			if ( p.x is >= -1 and < 1 && p.y is >= -1 and < 1 && p.z is >= -1 and < 1 )
				return volume.Name;
		}
		return null;
	}

	internal Destination[] Neighbors( string room )
	{
		var nav = Scene.NavMesh;
		if ( !nav.IsEnabled || nav.IsGenerating || nav.IsDirty )
		{
			connections.Clear();
			depths.Clear();
			return [];
		}
		if ( connections.TryGetValue( room, out var neighbors ) ) return neighbors;
		var source = Destinations.First( x => x.Name == room );
		var start = nav.GetClosestPoint( source.Position );
		if ( start is null ) return [];
		List<Destination> reachable = [];
		foreach ( var destination in Destinations )
		{
			if ( destination.Name == room || nav.GetClosestPoint( destination.Position ) is not { } endpoint ) continue;
			var path = nav.CalculatePath( new() { Start = start.Value, Target = endpoint } );
			if ( path.Status != NavMeshPathStatus.Complete || path.Points.Count == 0 ||
				LocationAt( path.Points[0].Position ) != room || LocationAt( path.Points[^1].Position ) != destination.Name ) continue;
			if ( path.Points.Any( p => LocationAt( p.Position ) is { } via && via != room && via != destination.Name ) ) continue;
			reachable.Add( destination with { Position = endpoint } );
		}
		return connections[room] = reachable.ToArray();
	}
}
