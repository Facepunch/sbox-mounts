using System;

namespace Overgrowth;

internal sealed partial class CharacterSurface
{
	internal sealed class Drip( int face )
	{
		internal int Face = face, EntryEdge = -1;
		internal Vector3 Weights = Vector3.One / 3f;
		internal float Amount = 0.1f;
		internal bool CanDrip;
	}

	int[] edgeNeighbors;

	internal Drip NearestDrip( Vector3[] posed, Vector3 point )
	{
		int closest = -1;
		float distance = float.MaxValue;
		for ( int i = 0; i < indices.Length; i += 3 )
		{
			var center = (posed[indices[i]] + posed[indices[i + 1]] + posed[indices[i + 2]]) / 3f;
			float d = center.DistanceSquared( point );
			if ( d >= distance ) continue;
			distance = d;
			closest = i;
		}
		return closest < 0 ? null : new( closest );
	}

	internal Drip DirectedDrip( Vector3 origin, Vector3 direction )
	{
		int closest = -1;
		float best = float.NegativeInfinity;
		for ( int i = 0; i < indices.Length; i += 3 )
		{
			var center = (vertices[indices[i]].Position + vertices[indices[i + 1]].Position + vertices[indices[i + 2]].Position) / 3f;
			float alignment = (center - origin).Normal.Dot( direction );
			if ( alignment <= best ) continue;
			best = alignment;
			closest = i;
		}
		return closest < 0 ? null : new( closest ) { Amount = 1f, CanDrip = true };
	}

	internal Vector3 DripPosition( Drip drip, Vector3[] posed ) =>
		posed[indices[drip.Face]] * drip.Weights.x + posed[indices[drip.Face + 1]] * drip.Weights.y + posed[indices[drip.Face + 2]] * drip.Weights.z;

	int[] FindNeighbors()
	{
		// Weld by bind position across UV seams. Only edges shared by exactly two faces connect.
		var ids = new Dictionary<Vector3, int>();
		var edges = new Dictionary<(int, int), (int First, int Count)>();
		var neighbors = new int[indices.Length];
		Array.Fill( neighbors, -1 );
		int VertexId( int corner )
		{
			var p = vertices[indices[corner]].Position;
			if ( !ids.TryGetValue( p, out var id ) ) ids.Add( p, id = ids.Count );
			return id;
		}
		for ( int face = 0; face < indices.Length; face += 3 )
		for ( int edge = 0; edge < 3; edge++ )
		{
			int i = face + edge;
			int a = VertexId( i ), b = VertexId( face + (edge + 1) % 3 );
			var key = a < b ? (a, b) : (b, a);
			if ( !edges.TryGetValue( key, out var match ) ) { edges.Add( key, (i, 1) ); continue; }
			edges[key] = (match.First, match.Count + 1);
			if ( match.Count == 1 ) { neighbors[i] = match.First; neighbors[match.First] = i; }
			else if ( match.Count == 2 ) { neighbors[neighbors[match.First]] = -1; neighbors[match.First] = -1; }
		}
		return neighbors;
	}

	internal bool AdvanceDrip( Drip drip, Vector3[] posed, List<Vector4> lines )
	{
		edgeNeighbors ??= FindNeighbors();
		float traveled = 0f;
		for ( int pass = 0; pass < 10 && traveled < 1f; pass++ )
		{
			int face = drip.Face;
			Vector3 Point( int i ) => (posed[indices[face + i]] - posed[0]) / 40f;
			var a = Point( 0 );
			var bPoint = Point( 1 );
			var cPoint = Point( 2 );
			var ab = bPoint - a;
			var ac = cPoint - a;
			var cross = ab.Cross( cPoint - bPoint );
			if ( cross.LengthSquared == 0f ) return true;
			var planeRight = (cross / cross.Length).Cross( ab );
			var right = planeRight / planeRight.Length;
			var up = ab / ab.Length;
			Vector2 Local( Vector3 v ) => new( v.Dot( right ), v.Dot( up ) );
			var b = Local( ab );
			var c = Local( ac );
			Vector2 Vertex( int i ) => i == 0 ? Vector2.Zero : i == 1 ? b : c;
			Vector2 Edge( int i ) => Vertex( (i + 1) % 3 ) - Vertex( i );
			static float Cross( Vector2 x, Vector2 y ) => x.x * y.y - x.y * y.x;
			var start = drip.Weights.y * b + drip.Weights.z * c;
			var direction = new Vector2( -right.z, -up.z ).Normal;
			int entry = drip.EntryEdge;
			bool alongEdge = entry >= 0 && Cross( direction, Edge( entry ) ) <= 0f;
			if ( alongEdge )
			{
				var axis = Edge( entry ).Normal;
				direction = axis * axis.Dot( direction );
			}
			var endPoint = start + direction * 0.005f;
			int exit = -1;
			float fraction = 1f;
			for ( int edge = 0; edge < 3; edge++ )
			{
				var line = Edge( edge );
				if ( Cross( direction, line ) >= 0f ) continue;
				var lineStart = Vertex( edge );
				var segment = (lineStart + line) - lineStart;
				float denom = Cross( endPoint - start, segment );
				if ( denom == 0f ) continue;
				float t = Cross( segment, start - lineStart ) / denom;
				if ( t < 0f || t > 1f || (exit >= 0 && t >= fraction) ) continue;
				exit = edge;
				fraction = t;
			}
			int neighbor = exit >= 0 ? edgeNeighbors[face + exit] : -1;
			if ( exit >= 0 && neighbor == -1 ) return true;
			if ( exit >= 0 && alongEdge ) return false;
			if ( exit >= 0 ) endPoint = start + direction * 0.005f * fraction;
			float denomUV = (b.y - c.y) * -c.x + (c.x - b.x) * -c.y;
			float firstWeight = ((b.y - c.y) * (endPoint.x - c.x) + (c.x - b.x) * (endPoint.y - c.y)) / denomUV;
			float secondWeight = (c.y * (endPoint.x - c.x) - c.x * (endPoint.y - c.y)) / denomUV;
			var end = new Vector3( firstWeight, secondWeight, 1f - firstWeight - secondWeight );
			Vector2 UV( Vector3 weights ) => uv[face] * weights.x + uv[face + 1] * weights.y + uv[face + 2] * weights.z;
			var startUV = UV( drip.Weights );
			var endUV = UV( end );
			lines.Add( new( startUV.x, startUV.y, endUV.x, endUV.y ) );
			if ( exit == -1 )
			{
				drip.Weights = end;
				if ( !alongEdge ) drip.EntryEdge = -1;
				return true;
			}
			int reverse = neighbor % 3;
			drip.Face = neighbor - reverse;
			drip.EntryEdge = reverse;
			bool swapped = vertices[indices[neighbor]].Position != vertices[indices[face + exit]].Position;
			float first = endPoint.Distance( Vertex( (exit + 1) % 3 ) ) / Edge( exit ).Length;
			float second = 1f - first;
			var weights = Vector3.Zero;
			weights[reverse] = swapped ? second : first;
			weights[(reverse + 1) % 3] = swapped ? first : second;
			drip.Weights = weights;
			traveled += fraction;
		}
		return true;
	}
}
