using System;
using System.Globalization;

namespace Renderware;

/// <summary>
/// Where the water is, as flat polygons in GTA units (3 or 4 corners, counter clockwise or not - water is drawn
/// double sided). Read from waterpro.dat (III / VC) or water.dat (San Andreas).
/// </summary>
public sealed class WaterData
{
	public List<Vector3[]> Polygons { get; } = new();

	/// <summary>
	/// III / VC data/waterpro.dat: a 128x128 grid of 32 unit blocks starting at (<paramref name="startX"/>, -2048),
	/// each holding the index of its water height or 0x80 for none. Outside the grid it's open sea at height 0.
	/// III's grid starts at -2048; Vice City's is shifted 400 units west (checked against the map: that's where its
	/// buildings stop standing in the water).
	/// </summary>
	public static WaterData ReadWaterPro( Stream stream, float startX = -2048 )
	{
		const float StartY = -2048, BlockSize = 32, Sea = 8000; // sea reaches well past the fog
		const int Count = 128;

		using var br = new BinaryReader( stream );
		int levels = Math.Clamp( br.ReadInt32(), 0, 48 );
		var heights = new float[48];
		for ( int i = 0; i < 48; i++ )
			heights[i] = br.ReadSingle();

		br.ReadBytes( 48 * 16 ); // per-level rects
		br.ReadBytes( 64 * 64 ); // coarse grid - the fine one has everything
		var blocks = br.ReadBytes( Count * Count ); // [x * 128 + y]

		bool TryGetHeight( int x, int y, out float height )
		{
			byte level = blocks[x * Count + y];
			height = level < levels ? heights[level] : 0;
			return (level & 0x80) == 0 && level < levels;
		}

		var water = new WaterData();

		// runs of equal height along x become one quad
		for ( int y = 0; y < Count; y++ )
		{
			for ( int x = 0; x < Count; )
			{
				if ( !TryGetHeight( x, y, out var height ) )
				{
					x++;
					continue;
				}

				int end = x + 1;
				while ( end < Count && TryGetHeight( end, y, out var next ) && next == height )
					end++;

				water.AddQuad( startX + x * BlockSize, StartY + y * BlockSize, startX + end * BlockSize, StartY + (y + 1) * BlockSize, height );
				x = end;
			}
		}

		// open sea around the grid
		float x0 = startX, x1 = startX + Count * BlockSize, y0 = StartY, y1 = StartY + Count * BlockSize;
		water.AddQuad( x0 - Sea, y0 - Sea, x1 + Sea, y0, 0 );
		water.AddQuad( x0 - Sea, y1, x1 + Sea, y1 + Sea, 0 );
		water.AddQuad( x0 - Sea, y0, x0, y1, 0 );
		water.AddQuad( x1, y0, x1 + Sea, y1, 0 );

		return water;
	}

	/// <summary>
	/// San Andreas data/water.dat: a "processed" line, then one polygon per line - 3 or 4 vertices of
	/// x y z + 4 wave values, then a flags value.
	/// </summary>
	public static WaterData ReadWaterDat( Stream stream )
	{
		var water = new WaterData();
		using var reader = new StreamReader( stream );

		while ( reader.ReadLine() is { } line )
		{
			var v = line.Split( (char[])[' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries );
			if ( v.Length < 22 )
				continue; // header, comments

			int corners = v.Length >= 29 ? 4 : 3;
			var polygon = new Vector3[corners];
			for ( int c = 0; c < corners; c++ )
			{
				float F( int i ) => float.Parse( v[c * 7 + i], NumberStyles.Float, CultureInfo.InvariantCulture );
				polygon[c] = new Vector3( F( 0 ), F( 1 ), F( 2 ) );
			}

			// quads are stored as a 2x2 grid of corners - put them in winding order
			if ( corners == 4 )
				polygon = [polygon[0], polygon[1], polygon[3], polygon[2]];

			water.Polygons.Add( polygon );
		}

		return water;
	}

	void AddQuad( float x0, float y0, float x1, float y1, float z ) =>
		Polygons.Add( [new( x0, y0, z ), new( x1, y0, z ), new( x1, y1, z ), new( x0, y1, z )] );
}
