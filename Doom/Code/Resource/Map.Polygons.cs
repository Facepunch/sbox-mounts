using System;
using Node = Doom.Wad.Node;

namespace Doom;

/// <summary>
/// Floor and ceiling polygons. Doom never stores these: the renderer fills the space between
/// walls. Here every sector's boundary edges are chained into closed loops using the linedefs' own
/// vertices, so floors meet the walls exactly, and the loops are triangulated with islands (pillars,
/// inner sectors) as holes. A sector that can't be traced falls back to its BSP leaves, which are
/// convex but only approximate the outline (the node builder rounds split points).
/// </summary>
partial class MapLoader
{
	private readonly record struct Pt( double X, double Y );

	/// <summary>Convex polygons per sector, in map units. Null for sectors with no area.</summary>
	private List<(float X, float Y)[]>[] _polygons;

	private const double Epsilon = 1e-7;

	private List<(float X, float Y)[]>[] BuildSectorPolygons()
	{
		var edges = new List<(int From, int To)>[_sectors.Count];

		for ( int line = 0; line < _lines.Count; line++ )
		{
			foreach ( bool front in (bool[])[true, false] )
			{
				int sector = SectorOf( line, front );
				if ( sector < 0 || SectorOf( line, !front ) == sector ) continue;

				var l = _lines[line];
				(edges[sector] ??= []).Add( front ? (l.StartVertex, l.EndVertex) : (l.EndVertex, l.StartVertex) );
			}
		}

		List<(float X, float Y)[]>[] bsp = null;
		var output = new List<(float X, float Y)[]>[_sectors.Count];

		for ( int i = 0; i < output.Length; i++ )
		{
			output[i] = edges[i] is null ? null : TriangulateSector( edges[i] );
			if ( output[i] is not null ) continue;

			bsp ??= BuildBspPolygons();
			if ( edges[i] is not null )
				Log.Warning( $"Doom: {Map.Name} sector {i} couldn't be triangulated from its outline, using BSP leaves" );

			output[i] = bsp[i];
		}

		return output;
	}

	private List<(float X, float Y)[]> TriangulateSector( List<(int From, int To)> edges )
	{
		var loops = TraceLoops( edges );
		if ( loops is null ) return null;

		var outlines = new List<List<Pt>>();
		var holes = new List<List<Pt>>();

		foreach ( var loop in loops )
		{
			var points = loop.Select( v => new Pt( _vertexes[v].X, _vertexes[v].Y ) ).ToList();

			double area = SignedArea( points );
			if ( Math.Abs( area ) < 1e-9 ) continue;

			// Loops run with the sector on the right: outlines are clockwise, islands counter-clockwise.
			// The triangulator wants outlines counter-clockwise and holes clockwise, so flip both.
			points.Reverse();
			(area < 0 ? outlines : holes).Add( points );
		}

		if ( outlines.Count == 0 ) return null;

		// Each hole belongs to the smallest outline containing it.
		var owned = new List<List<Pt>>[outlines.Count];
		foreach ( var hole in holes )
		{
			int owner = -1;
			double ownerArea = double.MaxValue;

			for ( int i = 0; i < outlines.Count; i++ )
			{
				if ( !HoleInside( hole, outlines[i] ) ) continue;

				double area = Math.Abs( SignedArea( outlines[i] ) );
				if ( area >= ownerArea ) continue;

				owner = i;
				ownerArea = area;
			}

			if ( owner < 0 ) return null;
			(owned[owner] ??= []).Add( hole );
		}

		var result = new List<(float X, float Y)[]>();
		for ( int i = 0; i < outlines.Count; i++ )
		{
			var triangles = Triangulate( outlines[i], owned[i] ?? [] );
			if ( triangles is null ) return null;
			result.AddRange( triangles );
		}

		return result;
	}

	// ------------------------------------------------------------------
	// Loop tracing
	// ------------------------------------------------------------------

	/// <summary>
	/// Chains edges into closed loops of vertex indices. Where several edges leave a vertex, the sharpest
	/// right turn is taken, which keeps the sector on the right and splits loops that merely touch.
	/// Returns null if an edge chain doesn't close.
	/// </summary>
	private List<List<int>> TraceLoops( List<(int From, int To)> edges )
	{
		var outgoing = new Dictionary<int, List<int>>();
		for ( int i = 0; i < edges.Count; i++ )
		{
			if ( !outgoing.TryGetValue( edges[i].From, out var list ) )
				outgoing[edges[i].From] = list = [];
			list.Add( i );
		}

		var used = new bool[edges.Count];
		var loops = new List<List<int>>();

		for ( int start = 0; start < edges.Count; start++ )
		{
			if ( used[start] ) continue;

			var loop = new List<int>();
			int current = start;
			used[start] = true;

			while ( true )
			{
				var (from, to) = edges[current];
				loop.Add( from );

				if ( !outgoing.TryGetValue( to, out var candidates ) ) return null;

				int next = -1;
				double best = double.MaxValue;

				foreach ( int c in candidates )
				{
					if ( used[c] && c != start ) continue;

					double turn = TurnAngle( from, to, edges[c].To );
					if ( turn >= best ) continue;

					best = turn;
					next = c;
				}

				if ( next < 0 ) return null;
				if ( next == start ) break;

				used[next] = true;
				current = next;
			}

			loops.Add( loop );
		}

		return loops;
	}

	/// <summary>Signed turn from edge from->to onto to->next. Negative is a right turn.</summary>
	private double TurnAngle( int from, int to, int next )
	{
		double ix = _vertexes[to].X - _vertexes[from].X;
		double iy = _vertexes[to].Y - _vertexes[from].Y;
		double ox = _vertexes[next].X - _vertexes[to].X;
		double oy = _vertexes[next].Y - _vertexes[to].Y;

		double cross = ix * oy - iy * ox;
		double dot = ix * ox + iy * oy;

		// A straight U-turn is the last choice.
		if ( Math.Abs( cross ) < 1e-9 && dot < 0 ) return Math.PI;

		return Math.Atan2( cross, dot );
	}

	// ------------------------------------------------------------------
	// Triangulation
	// ------------------------------------------------------------------

	private static List<(float X, float Y)[]> Triangulate( List<Pt> outline, List<List<Pt>> holes )
	{
		var polygon = new List<Pt>( outline );

		// Merge holes into the outline with a bridge each, rightmost hole first.
		foreach ( var hole in holes.OrderByDescending( MaxX ) )
		{
			if ( !Bridge( polygon, hole, outline, holes ) )
				return null;
		}

		return EarClip( polygon );
	}

	/// <summary>
	/// Connects a hole to the polygon with the shortest segment that stays inside the sector
	/// and crosses nothing, then splices the hole's vertices in at that point.
	/// </summary>
	private static bool Bridge( List<Pt> polygon, List<Pt> hole, List<Pt> outline, List<List<Pt>> allHoles )
	{
		var pairs = new List<(double Distance, int Hole, int Polygon)>( hole.Count * polygon.Count );

		for ( int h = 0; h < hole.Count; h++ )
		{
			for ( int p = 0; p < polygon.Count; p++ )
			{
				double dx = hole[h].X - polygon[p].X;
				double dy = hole[h].Y - polygon[p].Y;
				pairs.Add( (dx * dx + dy * dy, h, p) );
			}
		}

		pairs.Sort( ( a, b ) => a.Distance.CompareTo( b.Distance ) );

		foreach ( var (_, h, p) in pairs )
		{
			if ( !Visible( hole[h], polygon[p], polygon, allHoles, outline ) ) continue;

			var splice = new List<Pt>( hole.Count + 2 );
			for ( int k = 0; k < hole.Count; k++ )
				splice.Add( hole[(h + k) % hole.Count] );

			// Walk the whole hole, come back to its start, then back to the polygon vertex.
			splice.Add( hole[h] );
			splice.Add( polygon[p] );

			polygon.InsertRange( p + 1, splice );
			return true;
		}

		return false;
	}

	private static bool Visible( Pt a, Pt b, List<Pt> polygon, List<List<Pt>> holes, List<Pt> outline )
	{
		if ( CrossesAny( a, b, polygon ) || holes.Any( h => CrossesAny( a, b, h ) ) ) return false;

		var mid = new Pt( (a.X + b.X) / 2, (a.Y + b.Y) / 2 );
		return PointInPolygon( mid, outline ) && !holes.Any( h => PointInPolygon( mid, h ) );
	}

	private static bool CrossesAny( Pt a, Pt b, List<Pt> ring )
	{
		for ( int i = 0; i < ring.Count; i++ )
		{
			var c = ring[i];
			var d = ring[(i + 1) % ring.Count];

			if ( c == a || c == b || d == a || d == b ) continue;
			if ( SegmentsIntersect( a, b, c, d ) ) return true;
		}

		return false;
	}

	/// <summary>Ear clipping on a counter-clockwise polygon that may contain bridge duplicates.</summary>
	private static List<(float X, float Y)[]> EarClip( List<Pt> input )
	{
		var pts = new List<Pt>( input );
		var result = new List<(float X, float Y)[]>();
		int start = 0;

		while ( pts.Count > 3 )
		{
			int n = pts.Count;
			bool clipped = false;

			for ( int t = 0; t < n && !clipped; t++ )
			{
				int k = (start + t) % n;
				var a = pts[(k + n - 1) % n];
				var b = pts[k];
				var c = pts[(k + 1) % n];

				double area = Orient( a, b, c );

				if ( Math.Abs( area ) < 1e-9 )
				{
					// Zero-area sliver (collinear point or bridge spike): drop the vertex, no triangle.
					pts.RemoveAt( k );
					clipped = true;
				}
				else if ( area > 0 && IsEar( pts, a, b, c ) )
				{
					result.Add( Tri( a, b, c ) );
					pts.RemoveAt( k );
					clipped = true;
				}

				if ( clipped )
					start = pts.Count > 0 ? (k - 1 + pts.Count) % pts.Count : 0;
			}

			if ( !clipped ) return null;
		}

		if ( pts.Count == 3 && Math.Abs( Orient( pts[0], pts[1], pts[2] ) ) > 1e-9 )
			result.Add( Tri( pts[0], pts[1], pts[2] ) );

		return result;
	}

	private static bool IsEar( List<Pt> pts, Pt a, Pt b, Pt c )
	{
		foreach ( var p in pts )
		{
			if ( p == a || p == b || p == c ) continue;
			if ( InTriangle( p, a, b, c ) ) return false;
		}

		return true;
	}

	private static (float X, float Y)[] Tri( Pt a, Pt b, Pt c )
		=> [((float)a.X, (float)a.Y), ((float)b.X, (float)b.Y), ((float)c.X, (float)c.Y)];

	// ------------------------------------------------------------------
	// BSP leaves (fallback)
	// ------------------------------------------------------------------

	/// <summary>
	/// Each BSP leaf is a convex region. Start from a box around the map, cut it with every partition
	/// line on the way down to the leaf, then with the leaf's own segs (the sector is on their right).
	/// </summary>
	private List<(float X, float Y)[]>[] BuildBspPolygons()
	{
		var output = new List<(float X, float Y)[]>[_sectors.Count];
		if ( _subsectors.Count == 0 || _vertexes.Count == 0 ) return output;

		const double margin = 16;
		double minX = _vertexes.Min( v => v.X ) - margin, maxX = _vertexes.Max( v => v.X ) + margin;
		double minY = _vertexes.Min( v => v.Y ) - margin, maxY = _vertexes.Max( v => v.Y ) + margin;

		var box = new List<(double X, double Y)> { (minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY) };

		// The root is the last node. A map with no nodes is a single subsector.
		WalkBsp( _nodes.Count == 0 ? Node.SubsectorFlag : _nodes.Count - 1, box, output );
		return output;
	}

	private void WalkBsp( int child, List<(double X, double Y)> polygon, List<(float X, float Y)[]>[] output )
	{
		if ( (child & Node.SubsectorFlag) != 0 )
		{
			AddLeaf( child & ~Node.SubsectorFlag, polygon, output );
			return;
		}

		if ( child >= _nodes.Count ) return;

		var node = _nodes[child];
		var right = Clip( polygon, node.X, node.Y, node.DX, node.DY, keepRight: true );
		var left = Clip( polygon, node.X, node.Y, node.DX, node.DY, keepRight: false );

		if ( right.Count >= 3 ) WalkBsp( node.RightChild, right, output );
		if ( left.Count >= 3 ) WalkBsp( node.LeftChild, left, output );
	}

	private void AddLeaf( int index, List<(double X, double Y)> polygon, List<(float X, float Y)[]>[] output )
	{
		if ( index >= _subsectors.Count ) return;

		var subsector = _subsectors[index];
		if ( subsector.SegCount == 0 || subsector.FirstSeg >= _segs.Count ) return;

		int sector = SectorOfSeg( _segs[subsector.FirstSeg] );
		if ( sector < 0 ) return;

		for ( int i = 0; i < subsector.SegCount && polygon.Count >= 3; i++ )
		{
			int segIndex = subsector.FirstSeg + i;
			if ( segIndex >= _segs.Count ) break;

			var seg = _segs[segIndex];
			var p = _vertexes[seg.StartVertex];
			var q = _vertexes[seg.EndVertex];
			polygon = Clip( polygon, p.X, p.Y, q.X - p.X, q.Y - p.Y, keepRight: true );
		}

		if ( polygon.Count < 3 ) return;

		(output[sector] ??= []).Add( polygon.Select( p => ((float)p.X, (float)p.Y) ).ToArray() );
	}

	/// <summary>
	/// Clips a convex polygon to one side of a line. Doom's "right" of a directed line (ox, oy) + (dx, dy)
	/// is where dx*(py - oy) - dy*(px - ox) is negative.
	/// </summary>
	private static List<(double X, double Y)> Clip( List<(double X, double Y)> polygon,
		double ox, double oy, double dx, double dy, bool keepRight )
	{
		var result = new List<(double X, double Y)>( polygon.Count + 2 );
		double sign = keepRight ? -1 : 1;

		for ( int i = 0; i < polygon.Count; i++ )
		{
			var p = polygon[i];
			var q = polygon[(i + 1) % polygon.Count];

			double gp = sign * (dx * (p.Y - oy) - dy * (p.X - ox));
			double gq = sign * (dx * (q.Y - oy) - dy * (q.X - ox));
			bool pIn = gp >= -Epsilon;
			bool qIn = gq >= -Epsilon;

			if ( pIn ) AddPoint( result, p );

			if ( pIn != qIn )
			{
				double t = gp / (gp - gq);
				AddPoint( result, (p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t) );
			}
		}

		if ( result.Count > 1 && Near( result[0], result[^1] ) )
			result.RemoveAt( result.Count - 1 );

		return result;
	}

	private static void AddPoint( List<(double X, double Y)> list, (double X, double Y) point )
	{
		if ( list.Count == 0 || !Near( list[^1], point ) )
			list.Add( point );
	}

	private static bool Near( (double X, double Y) a, (double X, double Y) b )
		=> Math.Abs( a.X - b.X ) < 1e-6 && Math.Abs( a.Y - b.Y ) < 1e-6;

	// ------------------------------------------------------------------
	// Geometry helpers
	// ------------------------------------------------------------------

	private static double Orient( Pt a, Pt b, Pt c ) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

	private static bool InTriangle( Pt p, Pt a, Pt b, Pt c ) => Orient( a, b, p ) >= 0 && Orient( b, c, p ) >= 0 && Orient( c, a, p ) >= 0;

	private static double SignedArea( List<Pt> points )
	{
		double sum = 0;
		for ( int i = 0; i < points.Count; i++ )
		{
			var p = points[i];
			var q = points[(i + 1) % points.Count];
			sum += p.X * q.Y - q.X * p.Y;
		}

		return sum / 2;
	}

	private static double MaxX( List<Pt> points ) => points.Max( p => p.X );

	private static bool PointInPolygon( Pt point, List<Pt> polygon )
	{
		bool inside = false;

		for ( int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++ )
		{
			var a = polygon[i];
			var b = polygon[j];

			if ( (a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X )
				inside = !inside;
		}

		return inside;
	}

	private static bool HoleInside( List<Pt> hole, List<Pt> outline )
	{
		// A vertex shared with the outline is on its boundary, which makes the test ambiguous.
		foreach ( var p in hole )
		{
			if ( !outline.Contains( p ) )
				return PointInPolygon( p, outline );
		}

		var a = hole[0];
		var b = hole[1 % hole.Count];
		return PointInPolygon( new Pt( (a.X + b.X) / 2, (a.Y + b.Y) / 2 ), outline );
	}

	/// <summary>True if the segments cross or touch, including collinear overlap.</summary>
	private static bool SegmentsIntersect( Pt a, Pt b, Pt c, Pt d )
	{
		double o1 = Orient( a, b, c );
		double o2 = Orient( a, b, d );
		double o3 = Orient( c, d, a );
		double o4 = Orient( c, d, b );

		if ( ((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) && ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0)) )
			return true;

		return (o1 == 0 && OnSegment( a, b, c ))
			|| (o2 == 0 && OnSegment( a, b, d ))
			|| (o3 == 0 && OnSegment( c, d, a ))
			|| (o4 == 0 && OnSegment( c, d, b ));
	}

	private static bool OnSegment( Pt a, Pt b, Pt p )
		=> p.X >= Math.Min( a.X, b.X ) && p.X <= Math.Max( a.X, b.X )
		&& p.Y >= Math.Min( a.Y, b.Y ) && p.Y <= Math.Max( a.Y, b.Y );
}
