using Sandbox.Navigation;
using Sandbox.Npcs;

namespace NS2;

/// <summary>The named volumes and exploration destinations authored in a Spark level.</summary>
public sealed partial class MapLocations : Component
{
	public readonly record struct Volume( string Name, Transform Bounds )
	{
		internal bool Contains( Vector3 position )
		{
			var p = Bounds.PointToLocal( position );
			return p.x is >= -1 and < 1 && p.y is >= -1 and < 1 && p.z is >= -1 and < 1;
		}
	}
	public readonly record struct Destination( string Name, Vector3 Position, bool Strategic );
	[Property] public Volume[] Volumes { get; set; } = [];
	[Property] public Destination[] Destinations { get; set; } = [];
	[Property] internal Vector3[] ResourcePoints { get; set; } = [];
	[Property] internal Vector3[] TechPoints { get; set; } = [];

	internal bool BlocksStructure( Vector3 position )
	{
		const float nozzleRadius = 1.5f * MapLoader.Scale, techRadius = 3.2f * MapLoader.Scale;
		foreach ( var point in ResourcePoints )
			if ( position.DistanceSquared( point ) <= nozzleRadius * nozzleRadius ) return true;
		foreach ( var point in TechPoints )
			if ( position.DistanceSquared( point ) <= techRadius * techRadius ) return true;
		return false;
	}

	[Property] internal string AlienGridPath { get; set; }
	[Property] internal string GridPath { get; set; }
	[Property] internal float CellHeight { get; set; }
	HashSet<(string, string)> directPaths;
	bool readingGrid, requestedGrid;
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

	readonly Dictionary<string, int> playerBalance = new();
	float lastPlayerCount = -1;

	internal int AlienPlayerBalance( Vector3 position )
	{
		var room = LocationAt( position );
		if ( room is null ) return 0;
		if ( lastPlayerCount != Time.Now )
		{
			lastPlayerCount = Time.Now;
			playerBalance.Clear();
			foreach ( var npc in Scene.GetAllComponents<BaseNpc>() )
			{
				int count = npc switch { AlienLifeform => 1, Marine or Exo => -1, _ => 0 };
				if ( count == 0 || npc.Health <= 0 || LocationAt( npc.WorldPosition ) is not { } name ) continue;
				playerBalance[name] = playerBalance.GetValueOrDefault( name ) + count;
			}
		}
		return playerBalance.GetValueOrDefault( room );
	}

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
			if ( volume.Contains( position ) )
				return volume.Name;
		}
		return null;
	}

	internal int NearbyRoomOrder( string room, Vector3 lastSeen )
	{
		if ( string.IsNullOrEmpty( room ) ) return -1;
		var other = LocationAt( lastSeen );
		if ( string.IsNullOrEmpty( other ) ) return -1;
		if ( other == room ) return 0;
		var neighbors = Neighbors( room );
		for ( int i = 0; i < neighbors.Length; i++ )
			if ( neighbors[i].Name == other ) return i + 1;
		return -1;
	}

	Dictionary<string, Vector3[]> roomLights;

	internal Vector3[] LightsNear( Vector3 origin )
	{
		var room = LocationAt( origin );
		if ( room is null ) return [];
		if ( roomLights is null )
		{
			var lights = GetComponentsInChildren<Light>().Where( l => l.GameObject.Parent == GameObject && l is PointLight or SpotLight )
				.Select( l => l.WorldPosition ).ToArray();
			roomLights = Volumes.GroupBy( v => v.Name ).ToDictionary( g => g.Key,
				g => g.SelectMany( v => lights.Where( v.Contains ) ).ToArray() );
		}
		return roomLights[room];
	}

	Vector3? Project( Destination destination )
	{
		// A tech point can sit above another floor. Prefer a walkable approach on its own level.
		var bounds = BBox.FromPositionAndSize( destination.Position, new Vector3( 8, 8, 2 ) * MapLoader.Scale );
		var sameFloor = Scene.NavMesh.GetClosestPoint( bounds );
		if ( sameFloor is { } point && LocationAt( point ) == destination.Name ) return point;
		return Scene.NavMesh.GetClosestPoint( destination.Position );
	}

	async void ReadRoomRoutes( GameMount mount, float climb )
	{
		requestedGrid = true;
		readingGrid = true;
		try
		{
			var volumes = Volumes;
			var destinations = Destinations;
			float height = CellHeight;
			var bytes = mount.ReadAllBytes( GridPath );
			directPaths = await GameTask.RunInThreadAsync( () => SparkRoomRoutes.Build( bytes, volumes, destinations, height, climb ) );
		}
		catch ( System.Exception e ) { Log.Warning( e, $"Could not load exploration routes from {GridPath}" ); }
		finally { readingGrid = false; }
	}

	bool NavigationReady()
	{
		var nav = Scene.NavMesh;
		if ( nav.IsEnabled && !nav.IsGenerating && !nav.IsDirty ) return true;
		connections.Clear();
		depths.Clear();
		roomGateways.Clear();
		walkRoutes.Clear();
		return false;
	}

	internal Destination[] Neighbors( string room )
	{
		if ( !NavigationReady() ) return [];
		var nav = Scene.NavMesh;
		if ( connections.TryGetValue( room, out var neighbors ) ) return neighbors;
		if ( !requestedGrid && GridPath is not null && Sandbox.Mounting.Directory.Get( "ns2" ) is GameMount mount )
			ReadRoomRoutes( mount, nav.AgentStepSize );
		if ( readingGrid ) return [];
		if ( directPaths is not null )
		{
			List<Destination> goals = [];
			foreach ( var d in Destinations )
				if ( directPaths.Contains( (room, d.Name) ) && Project( d ) is { } p ) goals.Add( d with { Position = p } );
			return connections[room] = goals.ToArray();
		}
		var source = Destinations.FirstOrDefault( x => x.Name == room );
		if ( source.Name is null ) return [];
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
