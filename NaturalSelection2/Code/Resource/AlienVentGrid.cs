using System;

namespace NS2;

/// <summary>Vent floors in Spark's prebuilt small-agent Detour tile cache.</summary>
internal sealed class AlienVentGrid
{
	const float Cell = 0.2f * MapLoader.Scale;
	readonly HashSet<(int X, int Y, int Z)> floors = [];
	Vector3 origin;

	internal bool Contains( Vector3 position )
	{
		var cell = (position - origin) / Cell;
		int x = (int)MathF.Floor( cell.x ), y = (int)MathF.Floor( cell.y );
		// BotPathing_GetIsVent searches 0.6m above and below the player.
		for ( int z = (int)MathF.Ceiling( cell.z - 3 ); z <= MathF.Floor( cell.z + 3 ); z++ )
			if ( floors.Contains( (x, y, z) ) ) return true;
		return false;
	}

	internal AlienVentGrid( byte[] data )
	{
		SparkNavGrid.Read( data, ( tile, heights, areas, _ ) =>
		{
			int x = tile.X * tile.Width, y = tile.Y * tile.Height;
			if ( floors.Count == 0 ) origin = tile.Min - new Vector3( x, y, tile.BaseHeight ) * Cell;
			for ( int i = 0; i < areas.Length; i++ )
				if ( areas[i] == 8 ) // Pathing.PolyArea_Vent
					floors.Add( (x + i / tile.Height, y + i % tile.Height, tile.BaseHeight + heights[i]) );
		} );
	}
}
