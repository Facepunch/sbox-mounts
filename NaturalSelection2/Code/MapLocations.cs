using Sandbox.Navigation;

namespace NS2;

/// <summary>The named volumes and exploration destinations authored in a Spark level.</summary>
public sealed class MapLocations : Component
{
	public readonly record struct Volume( string Name, Transform Bounds );
	public readonly record struct Destination( string Name, Vector3 Position, bool Strategic );
	[Property] public Volume[] Volumes { get; set; } = [];
	[Property] public Destination[] Destinations { get; set; } = [];
	[Property] internal string AlienGridPath { get; set; }
	AlienVentGrid vents;
	bool readVents;

	internal bool IsVent( Vector3 position )
	{
		if ( !readVents && AlienGridPath is not null && Sandbox.Mounting.Directory.Get( "ns2" ) is GameMount mount )
		{
			readVents = true;
			try { vents = new( mount.ReadAllBytes( AlienGridPath ) ); }
			catch ( System.Exception e ) { Log.Warning( e, $"Could not load alien navigation from {AlienGridPath}" ); }
		}
		return vents?.Contains( position ) == true;
	}

	readonly Dictionary<string, Destination[]> connections = new();
	readonly Dictionary<string, Dictionary<string, int>> depths = new();
	readonly Dictionary<string, float> lastAlienVisit = new();

	internal void MarkVisited( string room ) => lastAlienVisit[room] = Time.Now;

	internal float ExplorationWeight( string room )
	{
		lastAlienVisit.TryGetValue( room, out float last );
		return System.Math.Clamp( (Time.Now - last) / 20, 0.5f, 1 );
	}

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

	Vector3? Project( Destination destination )
	{
		// A tech point can sit above another floor. Prefer a walkable approach on its own level.
		var bounds = BBox.FromPositionAndSize( destination.Position, new Vector3( 8, 8, 2 ) * MapLoader.Scale );
		var sameFloor = Scene.NavMesh.GetClosestPoint( bounds );
		if ( sameFloor is { } point && LocationAt( point ) == destination.Name ) return point;
		return Scene.NavMesh.GetClosestPoint( destination.Position );
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
		var start = Project( source );
		if ( start is null ) return [];
		List<Destination> reachable = [];
		foreach ( var destination in Destinations )
		{
			if ( destination.Name == room || Project( destination ) is not { } endpoint ) continue;
			var path = nav.CalculatePath( new() { Start = start.Value, Target = endpoint } );
			if ( path.Status != NavMeshPathStatus.Complete || path.Points.Count == 0 ||
				LocationAt( path.Points[0].Position ) != room || LocationAt( path.Points[^1].Position ) != destination.Name ) continue;
			if ( path.Points.Any( p => LocationAt( p.Position ) is { } via && via != room && via != destination.Name ) ) continue;
			reachable.Add( destination with { Position = endpoint } );
		}
		return connections[room] = reachable.ToArray();
	}
}
