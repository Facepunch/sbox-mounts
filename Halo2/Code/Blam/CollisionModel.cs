using System;
using System.Buffers.Binary;

namespace Halo2.Blam;

/// <summary>Builds bone-local hull hitboxes from the solid leaves of each default collision permutation.</summary>
internal static class CollisionModel
{
	public static void AddHitboxes( MapFile map, TagEntry tag, RenderModelTag model, ModelBuilder builder, float scale )
	{
		int header = map.VaToOffset( (int)tag.DataVirtualAddress );
		var materials = Rows( header + 0x14, 4 ).Select( Name ).ToArray();
		var nodes = Rows( header + 0x2c, 12 ).Select( Name ).ToArray();
		var bones = model.Nodes.Select( n => map.ResolveStringId( n.StringId ) ).ToHashSet();
		HitboxSetBuilder set = null;

		foreach ( int region in Rows( header + 0x1c, 12 ) )
		{
			var regionId = map.ReadInt32( region );
			var renderRegion = model.Regions.FirstOrDefault( r => r.StringId == regionId );
			var defaultId = renderRegion?.Permutations.FirstOrDefault()?.StringId;
			var permutations = Rows( region + 4, 20 );
			int permutation = permutations.FirstOrDefault( p => map.ReadInt32( p ) == defaultId, permutations.FirstOrDefault() );
			if ( permutation == 0 ) continue;

			foreach ( int bsp in Rows( permutation + 4, 68 ) )
			{
				int node = Short( bsp );
				if ( (uint)node >= nodes.Length || string.IsNullOrEmpty( nodes[node] ) || !bones.Contains( nodes[node] ) ) continue;

				var vertices = Rows( bsp + 0x3c, 16 ).Select( Point ).ToArray();
				if ( vertices.Length < 4 ) continue;
				var planes = Rows( bsp + 0xc, 16 ).Select( p => new Plane(
					new Vector3( Float( p ), Float( p + 4 ), Float( p + 8 ) ), Float( p + 12 ) * scale ) ).ToArray();
				var surfaceMaterials = new Dictionary<int, int>();
				foreach ( int surface in Rows( bsp + 0x2c, 8 ) )
				{
					if ( (map.ReadSpan( surface + 4, 1 )[0] & 0x10) == 0 )
						surfaceMaterials.TryAdd( Short( surface ) & 0x7fff, Short( surface + 6 ) );
				}
				if ( surfaceMaterials.Count == 0 ) continue;

				int tree = Table( bsp + 4 ), count = map.ReadInt32( bsp + 4 );
				int defaultMaterial = surfaceMaterials.Values.First();
				bool mixed = surfaceMaterials.Values.Any( m => m != defaultMaterial );
				string name = $"{Name( region )}_{nodes[node]}_{bsp - header}";
				int piece = 0;
				Visit( 0, BoxFaces( BBox.FromPoints( vertices ) ) );

				void Visit( int child, List<Face> cell )
				{
					// Halo 2 stores signed 24-bit child indices. -1 is solid,
					// other negative children reference leaves in empty space.
					if ( cell.Count < 4 ) return;
					if ( child == 0xffffff )
					{
						var points = cell.SelectMany( f => f.Points ).Distinct().ToArray();
						if ( !mixed )
						{
							AddHull( points, defaultMaterial );
							return;
						}

						// A hitbox has one tag. Tile the cell with face pyramids
						// so a trace receives the material of its entry plane.
						var center = Average( points );
						foreach ( var face in cell )
							AddHull( [.. face.Points, center], surfaceMaterials.GetValueOrDefault( face.Plane, defaultMaterial ) );
						return;
					}
					if ( (uint)child >= count ) return;
					int entry = tree + child * 8;
					int index = Short( entry );
					if ( (uint)index >= planes.Length ) return;
					for ( int side = 0; side < 2; side++ )
					{
						var bytes = map.ReadSpan( entry + 2 + 3 * side, 3 );
						int next = bytes[0] | bytes[1] << 8 | bytes[2] << 16;
						if ( next != 0xffffff && (uint)next >= count ) continue;
						Visit( next, Clip( cell, planes[index], index, side == 0 ? 1 : -1 ) );
					}
				}

				void AddHull( Vector3[] points, int material )
				{
					if ( points.Length < 4 ) return;
					set ??= builder.AddHitboxSet();
					try
					{
						var hitbox = set.AddHull( $"{name}_{piece++}", nodes[node], points );
						if ( (uint)material < materials.Length && !string.IsNullOrWhiteSpace( materials[material] ) )
							hitbox.AddTag( materials[material] );
					}
					catch ( ArgumentException )
					{
						// The cell only touches the model at a point, edge or flat face.
					}
				}
			}
		}

		string Name( int address ) => map.ResolveStringId( map.ReadInt32( address ) );
		int Short( int address ) => BinaryPrimitives.ReadInt16LittleEndian( map.ReadSpan( address, 2 ) );
		int Table( int block ) => map.VaToOffset( map.ReadInt32( block + 4 ) );
		Vector3 Point( int address ) => new Vector3( Float( address ), Float( address + 4 ), Float( address + 8 ) ) * scale;
		float Float( int address ) => BitConverter.Int32BitsToSingle( map.ReadInt32( address ) );

		IEnumerable<int> Rows( int block, int stride )
		{
			int count = map.ReadInt32( block ), address = Table( block );
			for ( int i = 0; i < count; i++ )
				yield return address + i * stride;
		}
	}

	readonly record struct Face( int Plane, Vector3[] Points );

	static List<Face> BoxFaces( BBox box )
	{
		// Start just outside the vertices so even axis-aligned boundary faces
		// acquire their original BSP plane when clipped.
		box = box.Grow( 0.01f );
		var a = box.Mins;
		var b = box.Maxs;
		Vector3[] v = [a, new( b.x, a.y, a.z ), new( b.x, b.y, a.z ), new( a.x, b.y, a.z ),
			new( a.x, a.y, b.z ), new( b.x, a.y, b.z ), b, new( a.x, b.y, b.z )];
		Face Quad( int i, int j, int k, int l ) => new( -1, [v[i], v[j], v[k], v[l]] );
		return [Quad( 0, 1, 2, 3 ), Quad( 4, 5, 6, 7 ), Quad( 0, 1, 5, 4 ),
			Quad( 2, 3, 7, 6 ), Quad( 0, 3, 7, 4 ), Quad( 1, 2, 6, 5 )];
	}

	static Vector3 Average( IEnumerable<Vector3> points )
	{
		var sum = Vector3.Zero;
		int count = 0;
		foreach ( var p in points ) { sum += p; count++; }
		return sum / count;
	}

	static List<Face> Clip( List<Face> cell, Plane plane, int index, int side )
	{
		var result = new List<Face>();
		var cap = new List<Vector3>();
		foreach ( var face in cell )
		{
			var polygon = new List<Vector3>( face.Points.Length + 1 );
			var a = face.Points[^1];
			float da = plane.GetDistance( a ) * side;
			foreach ( var b in face.Points )
			{
				float db = plane.GetDistance( b ) * side;
				if ( (da <= 0) != (db <= 0) )
				{
					var point = a + (b - a) * (da / (da - db));
					polygon.Add( point );
					if ( !cap.Any( v => (v - point).LengthSquared < 1e-8f ) ) cap.Add( point );
				}
				if ( db <= 0 ) polygon.Add( b );
				a = b;
				da = db;
			}
			if ( polygon.Count >= 3 ) result.Add( new( face.Plane, [.. polygon] ) );
		}

		if ( cap.Count >= 3 )
		{
			var center = Average( cap );
			var x = (cap[0] - center).Normal;
			var y = Vector3.Cross( plane.Normal, x );
			result.Add( new( index, cap.OrderBy( p => MathF.Atan2( (p - center).Dot( y ), (p - center).Dot( x ) ) ).ToArray() ) );
		}
		return result;
	}
}