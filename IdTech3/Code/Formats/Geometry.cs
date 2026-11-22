namespace IdTech3.Formats;

public static class Geometry
{
	/// <summary>
	/// Make triangle winding consistent: every output triangle's geometric normal (b-a)x(c-a)
	/// points along the vertex normals ("counter-clockwise from the front").
	/// idTech3 stores clockwise triangles, so this flips Q3 data.
	/// </summary>
	public static void OrientTriangles( IReadOnlyList<BspVertex> verts, List<int> indices, int start = 0 )
	{
		double vote = 0;
		for ( int i = start; i + 2 < indices.Count; i += 3 )
		{
			var a = verts[indices[i]]; var b = verts[indices[i + 1]]; var c = verts[indices[i + 2]];
			var n = (b.Position - a.Position).Cross( c.Position - a.Position );
			vote += n.Dot( a.Normal + b.Normal + c.Normal );
		}
		if ( vote >= 0 ) return;
		for ( int i = start; i + 2 < indices.Count; i += 3 )
			(indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
	}

	static BspVertex Lerp3( in BspVertex a, in BspVertex b, in BspVertex c, float t )
	{
		// quadratic bezier
		float i0 = (1 - t) * (1 - t), i1 = 2 * t * (1 - t), i2 = t * t;
		return Combine( a, i0, b, i1, c, i2 );
	}

	static BspVertex Combine( in BspVertex a, float wa, in BspVertex b, float wb, in BspVertex c, float wc )
	{
		var v = new BspVertex
		{
			Position = a.Position * wa + b.Position * wb + c.Position * wc,
			TexCoord = a.TexCoord * wa + b.TexCoord * wb + c.TexCoord * wc,
			LightmapCoord = a.LightmapCoord * wa + b.LightmapCoord * wb + c.LightmapCoord * wc,
			Normal = a.Normal * wa + b.Normal * wb + c.Normal * wc,
		};
		v.Color = new Color32(
			ClampByte( a.Color.r * wa + b.Color.r * wb + c.Color.r * wc ),
			ClampByte( a.Color.g * wa + b.Color.g * wb + c.Color.g * wc ),
			ClampByte( a.Color.b * wa + b.Color.b * wb + c.Color.b * wc ),
			ClampByte( a.Color.a * wa + b.Color.a * wb + c.Color.a * wc ) );
		return v;
	}

	static byte ClampByte( float f ) => (byte)Math.Clamp( (int)MathF.Round( f ), 0, 255 );

	static int CurveLevel( Vector3 p0, Vector3 p1, Vector3 p2, float tolerance, int maxLevel )
	{
		var mid = (p0 + p2) * 0.5f;
		float dev = (p1 - mid).Length * 0.5f;
		if ( dev < 0.1f ) return 1;
		int n = (int)MathF.Ceiling( MathF.Sqrt( dev / tolerance ) );
		return Math.Clamp( Math.Max( n, 2 ), 2, maxLevel );
	}

	static bool IsValidPatch( IReadOnlyList<BspVertex> control, int width, int height ) =>
		width >= 3 && height >= 3 && (width & 1) == 1 && (height & 1) == 1 && control.Count >= width * height;

	/// <summary>
	/// Subdivisions per sub-patch column (U) and row (V) of a patch, from its curvature, or null for a bad patch.
	/// A column's level is the most any of its rows needs.
	/// </summary>
	public static (int[] U, int[] V)? PatchLevels( IReadOnlyList<BspVertex> control, int width, int height, float tolerance = 4f, int maxLevel = 16 )
	{
		if ( !IsValidPatch( control, width, height ) ) return null;

		int pw = (width - 1) / 2, ph = (height - 1) / 2;
		var levelU = new int[pw];
		var levelV = new int[ph];

		for ( int i = 0; i < pw; i++ )
			for ( int row = 0; row < height; row++ )
			{
				int b = row * width + i * 2;
				levelU[i] = Math.Max( levelU[i], CurveLevel( control[b].Position, control[b + 1].Position, control[b + 2].Position, tolerance, maxLevel ) );
			}

		for ( int j = 0; j < ph; j++ )
			for ( int col = 0; col < width; col++ )
			{
				int b = j * 2 * width + col;
				levelV[j] = Math.Max( levelV[j], CurveLevel( control[b].Position, control[b + width].Position, control[b + width * 2].Position, tolerance, maxLevel ) );
			}

		return (levelU, levelV);
	}

	public sealed class PatchInfo
	{
		public IReadOnlyList<BspVertex> Control;
		public int Width, Height;
		public int[] U, V;
	}

	readonly record struct EdgeKey( int Ax, int Ay, int Az, int Mx, int My, int Mz, int Bx, int By, int Bz );

	/// <summary>
	/// Give patches that share an edge the same subdivision along it, as R_FixSharedVertexLodError does.
	/// Each patch picks its levels from its own curvature, so two patches meeting along a curve (a wall and the
	/// floor beside it) would otherwise split the shared edge differently and leave cracks between them.
	/// </summary>
	public static void StitchPatchLevels( IReadOnlyList<PatchInfo> patches )
	{
		// every border segment (three control points), keyed the same whichever way round it runs
		var edges = new Dictionary<EdgeKey, List<(PatchInfo Patch, bool U, int Index)>>();

		static (int, int, int) Quantize( Vector3 p ) =>
			((int)MathF.Round( p.x * 8 ), (int)MathF.Round( p.y * 8 ), (int)MathF.Round( p.z * 8 ));

		void AddEdge( PatchInfo patch, bool u, int index, Vector3 a, Vector3 m, Vector3 b )
		{
			var qa = Quantize( a ); var qm = Quantize( m ); var qb = Quantize( b );
			if ( qb.CompareTo( qa ) < 0 ) (qa, qb) = (qb, qa);
			var key = new EdgeKey( qa.Item1, qa.Item2, qa.Item3, qm.Item1, qm.Item2, qm.Item3, qb.Item1, qb.Item2, qb.Item3 );
			if ( !edges.TryGetValue( key, out var list ) ) edges[key] = list = [];
			list.Add( (patch, u, index) );
		}

		foreach ( var p in patches )
		{
			int w = p.Width, h = p.Height;
			var c = p.Control;
			for ( int i = 0; i < p.U.Length; i++ )
				foreach ( int row in new[] { 0, h - 1 } )
				{
					int b = row * w + i * 2;
					AddEdge( p, true, i, c[b].Position, c[b + 1].Position, c[b + 2].Position );
				}
			for ( int j = 0; j < p.V.Length; j++ )
				foreach ( int col in new[] { 0, w - 1 } )
				{
					int b = j * 2 * w + col;
					AddEdge( p, false, j, c[b].Position, c[b + w].Position, c[b + w * 2].Position );
				}
		}

		// a column's level covers both its border rows, so raising one can reach further neighbours: repeat until settled
		bool changed = true;
		for ( int pass = 0; changed && pass < 64; pass++ )
		{
			changed = false;
			foreach ( var list in edges.Values )
			{
				if ( list.Count < 2 ) continue;
				int max = 0;
				foreach ( var (patch, u, index) in list ) max = Math.Max( max, u ? patch.U[index] : patch.V[index] );
				foreach ( var (patch, u, index) in list )
				{
					var levels = u ? patch.U : patch.V;
					if ( levels[index] == max ) continue;
					levels[index] = max;
					changed = true;
				}
			}
		}
	}

	/// <summary>
	/// Tessellate a biquadratic Bézier patch of <paramref name="width"/> x <paramref name="height"/>
	/// control points. Levels are chosen per patch column/row so neighbouring sub-patches never crack; pass
	/// <paramref name="levels"/> (from <see cref="PatchLevels"/>, stitched with neighbouring patches) to use those.
	/// </summary>
	public static void TessellatePatch( IReadOnlyList<BspVertex> control, int width, int height,
		List<BspVertex> outVerts, List<int> outIndices, float tolerance = 4f, int maxLevel = 16, (int[] U, int[] V)? levels = null )
	{
		levels ??= PatchLevels( control, width, height, tolerance, maxLevel );
		if ( levels is not { } l ) return;
		int[] levelU = l.U, levelV = l.V;
		int pw = levelU.Length, ph = levelV.Length;

		int cols = 1, rows = 1;
		foreach ( var lu in levelU ) cols += lu;
		foreach ( var lv in levelV ) rows += lv;

		// map global column -> (patch, t)
		var uPatch = new int[cols]; var uT = new float[cols];
		{
			int c = 0;
			for ( int i = 0; i < pw; i++ )
				for ( int k = 0; k < levelU[i] + (i == pw - 1 ? 1 : 0); k++, c++ )
				{ uPatch[c] = i; uT[c] = (float)k / levelU[i]; }
		}
		var vPatch = new int[rows]; var vT = new float[rows];
		{
			int r = 0;
			for ( int j = 0; j < ph; j++ )
				for ( int k = 0; k < levelV[j] + (j == ph - 1 ? 1 : 0); k++, r++ )
				{ vPatch[r] = j; vT[r] = (float)k / levelV[j]; }
		}

		int baseIndex = outVerts.Count;
		for ( int r = 0; r < rows; r++ )
		{
			int j = vPatch[r];
			float t = vT[r];
			for ( int c = 0; c < cols; c++ )
			{
				int i = uPatch[c];
				float s = uT[c];
				int b = j * 2 * width + i * 2;

				// evaluate three rows along u then combine along v
				var r0 = Lerp3( control[b], control[b + 1], control[b + 2], s );
				var r1 = Lerp3( control[b + width], control[b + width + 1], control[b + width + 2], s );
				var r2 = Lerp3( control[b + width * 2], control[b + width * 2 + 1], control[b + width * 2 + 2], s );
				var v = Lerp3( r0, r1, r2, t );
				v.Normal = v.Normal.Normal;
				outVerts.Add( v );
			}
		}

		int startIndex = outIndices.Count;
		for ( int r = 0; r < rows - 1; r++ )
			for ( int c = 0; c < cols - 1; c++ )
			{
				int a = baseIndex + r * cols + c;
				int b = a + 1;
				int d = a + cols;
				int e = d + 1;
				outIndices.Add( a ); outIndices.Add( d ); outIndices.Add( b );
				outIndices.Add( b ); outIndices.Add( d ); outIndices.Add( e );
			}

		// fix up winding to agree with interpolated normals
		var all = outVerts;
		OrientTriangles( all, outIndices, startIndex );
	}

	/// <summary>
	/// Corner points of the convex volume bounded by planes (inside: dot(n, p) &lt;= d).
	/// </summary>
	public static List<Vector3> BrushPoints( IReadOnlyList<BspBrushSide> sides, float epsilon = 0.05f )
	{
		var points = new List<Vector3>();
		int n = sides.Count;
		if ( n < 4 ) return points;

		for ( int i = 0; i < n - 2; i++ )
		{
			var a = sides[i];
			for ( int j = i + 1; j < n - 1; j++ )
			{
				var b = sides[j];
				var ab = a.Normal.Cross( b.Normal );
				if ( ab.Dot( ab ) < 1e-10f ) continue;

				for ( int k = j + 1; k < n; k++ )
				{
					var c = sides[k];
					float det = ab.Dot( c.Normal );
					if ( MathF.Abs( det ) < 1e-6f ) continue;

					// p = (d1 (n2 x n3) + d2 (n3 x n1) + d3 (n1 x n2)) / det
					var p = (b.Normal.Cross( c.Normal ) * a.Dist + c.Normal.Cross( a.Normal ) * b.Dist + ab * c.Dist) * (1f / det);
					if ( float.IsNaN( p.x ) || MathF.Abs( p.x ) > 1e6f || MathF.Abs( p.y ) > 1e6f || MathF.Abs( p.z ) > 1e6f ) continue;

					bool inside = true;
					for ( int m = 0; m < n; m++ )
					{
						if ( sides[m].Normal.Dot( p ) - sides[m].Dist > epsilon ) { inside = false; break; }
					}
					if ( !inside ) continue;

					bool dup = false;
					foreach ( var q in points )
						if ( (q - p).Dot( q - p ) < 0.01f ) { dup = true; break; }
					if ( !dup ) points.Add( p );
				}
			}
		}

		// must be a real volume
		if ( points.Count < 4 ) return new List<Vector3>();
		var mins = points[0]; var maxs = points[0];
		foreach ( var p in points ) { mins = Vector3.Min( mins, p ); maxs = Vector3.Max( maxs, p ); }
		var ext = maxs - mins;
		if ( ext.x < 0.1f || ext.y < 0.1f || ext.z < 0.1f ) return new List<Vector3>();
		return points;
	}
}
