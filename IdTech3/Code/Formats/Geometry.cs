using System;
using System.Collections.Generic;

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
		v.Color = new Rgba(
			ClampByte( a.Color.R * wa + b.Color.R * wb + c.Color.R * wc ),
			ClampByte( a.Color.G * wa + b.Color.G * wb + c.Color.G * wc ),
			ClampByte( a.Color.B * wa + b.Color.B * wb + c.Color.B * wc ),
			ClampByte( a.Color.A * wa + b.Color.A * wb + c.Color.A * wc ) );
		return v;
	}

	static byte ClampByte( float f ) => (byte)Math.Clamp( (int)MathF.Round( f ), 0, 255 );

	static int CurveLevel( Vec3 p0, Vec3 p1, Vec3 p2, float tolerance, int maxLevel )
	{
		var mid = (p0 + p2) * 0.5f;
		float dev = (p1 - mid).Length * 0.5f;
		if ( dev < 0.1f ) return 1;
		int n = (int)MathF.Ceiling( MathF.Sqrt( dev / tolerance ) );
		return Math.Clamp( Math.Max( n, 2 ), 2, maxLevel );
	}

	/// <summary>
	/// Tessellate a biquadratic Bézier patch of <paramref name="width"/> x <paramref name="height"/>
	/// control points. Levels are chosen per patch column/row so neighbouring sub-patches never crack.
	/// </summary>
	public static void TessellatePatch( IReadOnlyList<BspVertex> control, int width, int height,
		List<BspVertex> outVerts, List<int> outIndices, float tolerance = 4f, int maxLevel = 16 )
	{
		if ( width < 3 || height < 3 || (width & 1) == 0 || (height & 1) == 0 || control.Count < width * height )
			return;

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

		int cols = 1, rows = 1;
		foreach ( var l in levelU ) cols += l;
		foreach ( var l in levelV ) rows += l;

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
				v.Normal = v.Normal.Normalized;
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
	public static List<Vec3> BrushPoints( IReadOnlyList<BspBrushSide> sides, float epsilon = 0.05f )
	{
		var points = new List<Vec3>();
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
					if ( float.IsNaN( p.X ) || MathF.Abs( p.X ) > 1e6f || MathF.Abs( p.Y ) > 1e6f || MathF.Abs( p.Z ) > 1e6f ) continue;

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
		if ( points.Count < 4 ) return new List<Vec3>();
		var mins = points[0]; var maxs = points[0];
		foreach ( var p in points ) { mins = Vec3.Min( mins, p ); maxs = Vec3.Max( maxs, p ); }
		var ext = maxs - mins;
		if ( ext.X < 0.1f || ext.Y < 0.1f || ext.Z < 0.1f ) return new List<Vec3>();
		return points;
	}
}
