using System;

namespace NS2;

/// <summary>Finds the named rooms crossed by shortest routes on Spark's default-agent floors.</summary>
internal sealed class SparkRoomRoutes
{
	sealed class Region( int room )
	{
		public readonly int Room = room;
		public Vector3 Center;
		public readonly HashSet<int> Links = [];
	}

	readonly record struct Floor( int Region, float Z, int Portals );
	readonly List<Region> regions = [];
	readonly Dictionary<(int, int), List<Floor>> borders = [];
	(Vector3 Position, int Room, int Group)[] cells = [];
	int[] queue = [];
	readonly int[] anchors;
	readonly float[] nearest;

	internal static HashSet<(string, string)> Build( byte[] data, MapLocations.Volume[] volumes,
		MapLocations.Destination[] destinations, float cellHeight, float climb )
	{
		var graph = new SparkRoomRoutes( destinations.Length );
		var ids = destinations.Select( (d, i) => (d.Name, i) ).ToDictionary( d => d.Name, d => d.i );
		var bounds = new BBox( -Vector3.One, Vector3.One );
		var rooms = volumes.Select( v => (Volume: v, Box: bounds.Transform( v.Bounds ), Room: ids[v.Name]) ).ToArray();
		var candidates = new List<(MapLocations.Volume Volume, BBox Box, int Room)>( rooms.Length );
		SparkNavGrid.Read( data, ( layer, heights, areas, connections ) =>
		{
			candidates.Clear();
			foreach ( var room in rooms )
				if ( room.Box.Mins.x <= layer.Max.x && room.Box.Maxs.x >= layer.Min.x && room.Box.Mins.y <= layer.Max.y && room.Box.Maxs.y >= layer.Min.y )
					candidates.Add( room );
			graph.AddLayer( layer, heights, areas, connections, cellHeight, climb, candidates, destinations );
		} );
		return graph.DirectPaths( destinations );
	}

	SparkRoomRoutes( int rooms )
	{
		anchors = new int[rooms];
		nearest = new float[rooms];
		Array.Fill( anchors, -1 );
		Array.Fill( nearest, float.PositiveInfinity );
	}

	void AddLayer( SparkNavGrid.Layer layer, ReadOnlySpan<byte> heights, ReadOnlySpan<byte> areas, ReadOnlySpan<byte> connections,
		float cellHeight, float climb, List<(MapLocations.Volume Volume, BBox Box, int Room)> volumes, MapLocations.Destination[] destinations )
	{
		int count = areas.Length, stride = layer.Height;
		if ( cells.Length < count )
		{
			cells = new (Vector3, int, int)[count];
			queue = new int[count];
		}
		float dx = (layer.Max.x - layer.Min.x) / layer.Width, dy = (layer.Max.y - layer.Min.y) / stride;
		// Flood each connected part of a room in this layer into a single routing node.
		for ( int i = 0; i < count; i++ )
		{
			cells[i] = (default, 0, -1);
			if ( areas[i] == 0 ) continue;
			var p = layer.Min + new Vector3( (i / stride + 0.5f) * dx, (i % stride + 0.5f) * dy, heights[i] * cellHeight );
			cells[i] = (p, -1, -1);
			foreach ( var volume in volumes )
			{
				if ( volume.Box.Contains( p ) && volume.Volume.Contains( p ) )
				{
					cells[i].Room = volume.Room;
					break;
				}
			}
		}

		for ( int i = 0; i < count; i++ )
		{
			if ( areas[i] == 0 || cells[i].Group >= 0 ) continue;
			int id = regions.Count, room = cells[i].Room;
			var region = new Region( room );
			regions.Add( region );
			cells[i].Group = id;
			queue[0] = i;
			int length = 1;
			for ( int k = 0; k < length; k++ )
			{
				int cell = queue[k], x = cell / stride, y = cell % stride;
				var p = cells[cell].Position;
				region.Center += p;
				if ( room >= 0 )
				{
					float distance = p.DistanceSquared( destinations[room].Position );
					if ( distance < nearest[room] ) { nearest[room] = distance; anchors[room] = id; }
				}
				int links = connections[cell];
				for ( int dir = 0; dir < 4; dir++ )
				{
					if ( (links & (1 << dir)) == 0 ) continue;
					var (stepX, stepY) = Direction( dir );
					int nx = x + stepX, ny = y + stepY;
					if ( (uint)nx >= layer.Width || (uint)ny >= stride ) continue;
					int n = nx * stride + ny;
					if ( cells[n].Group >= 0 )
					{
						if ( cells[n].Group != id ) Link( id, cells[n].Group );
					}
					else if ( cells[n].Room == room )
					{
						cells[n].Group = id;
						queue[length++] = n;
					}
				}
				if ( links >= 16 ) JoinBorders( layer.X * layer.Width + x, layer.Y * stride + y, id, p.z, links >> 4, climb );
			}
			region.Center /= length;
		}
	}

	// Detour numbers edges in Spark axes. Mounted X is Spark Z and mounted Y is Spark X.
	static (int X, int Y) Direction( int dir ) => dir switch { 0 => (0, -1), 1 => (1, 0), 2 => (0, 1), _ => (-1, 0) };

	void JoinBorders( int x, int y, int id, float z, int portals, float climb )
	{
		for ( int dir = 0; dir < 4; dir++ )
		{
			if ( (portals & (1 << dir)) == 0 ) continue;
			var (dx, dy) = Direction( dir );
			if ( !borders.TryGetValue( (x + dx, y + dy), out var adjacent ) ) continue;
			foreach ( var floor in adjacent )
				if ( (floor.Portals & (1 << (dir ^ 2))) != 0 && MathF.Abs( floor.Z - z ) <= climb + 0.01f ) Link( id, floor.Region );
		}
		if ( !borders.TryGetValue( (x, y), out var floors ) ) borders[(x, y)] = floors = [];
		floors.Add( new( id, z, portals ) );
	}

	void Link( int a, int b )
	{
		if ( regions[a].Links.Add( b ) ) regions[b].Links.Add( a );
	}

	HashSet<(string, string)> DirectPaths( MapLocations.Destination[] rooms )
	{
		HashSet<(string, string)> result = [];
		var distances = new float[regions.Count];
		var previous = new int[regions.Count];
		var queue = new PriorityQueue<int, float>();
		for ( int i = 0; i < rooms.Length; i++ )
		{
			int start = anchors[i];
			if ( start < 0 ) continue;
			Array.Fill( distances, float.PositiveInfinity );
			distances[start] = 0;
			queue.Enqueue( start, 0 );
			while ( queue.TryDequeue( out int node, out float distance ) )
			{
				if ( distance > distances[node] ) continue;
				foreach ( int next in regions[node].Links )
				{
					float cost = distance + regions[node].Center.Distance( regions[next].Center );
					if ( cost >= distances[next] ) continue;
					distances[next] = cost;
					previous[next] = node;
					queue.Enqueue( next, cost );
				}
			}
			for ( int j = 0; j < rooms.Length; j++ )
			{
				int node = anchors[j];
				if ( i == j || node < 0 || !float.IsFinite( distances[node] ) ) continue;
				while ( node != start && (regions[node].Room < 0 || regions[node].Room == i || regions[node].Room == j) )
					node = previous[node];
				if ( node == start ) result.Add( (rooms[i].Name, rooms[j].Name) );
			}
		}
		return result;
	}
}
