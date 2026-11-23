using System;
using System.Buffers.Binary;
using System.IO;

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
		using var reader = new BinaryReader( new MemoryStream( data ) );
		reader.BaseStream.Position = 16; // cache checksum
		int slots = checked(reader.ReadInt32() * reader.ReadInt32() * reader.ReadInt32());
		if ( slots < 0 || slots > (data.Length - 28) / 4 ) throw new InvalidDataException( "Invalid tile count" );
		Span<byte> cells = stackalloc byte[32 * 32 * 3];
		for ( int i = 0; i < slots; i++ )
		{
			int size = reader.ReadInt32();
			if ( size == 0 ) continue;
			int offset = (int)reader.BaseStream.Position;
			if ( size < 56 || size > data.Length - offset ) throw new InvalidDataException( "Invalid tile size" );
			var layer = data.AsSpan( offset, size );
			if ( !layer[..4].SequenceEqual( "RLTD"u8 ) || BinaryPrimitives.ReadInt32LittleEndian( layer[4..] ) != 1
				|| layer[48] != 32 || layer[49] != 32 ) throw new InvalidDataException( "Unsupported tile layer" );
			reader.BaseStream.Position += size;
			int tileY = BinaryPrimitives.ReadInt32LittleEndian( layer[8..] );
			int tileX = BinaryPrimitives.ReadInt32LittleEndian( layer[12..] );
			int height = BinaryPrimitives.ReadUInt16LittleEndian( layer[44..] );
			if ( floors.Count == 0 )
			{
				var min = new Vector3( BitConverter.ToSingle( layer[28..] ), BitConverter.ToSingle( layer[20..] ), BitConverter.ToSingle( layer[24..] ) );
				origin = min * MapLoader.Scale - new Vector3( tileX * 32, tileY * 32, height ) * Cell;
			}
			Expand( layer[56..], cells );
			for ( int j = 0; j < 1024; j++ )
			{
				if ( cells[1024 + j] == 8 ) // Pathing.PolyArea_Vent
					floors.Add( (tileX * 32 + j / 32, tileY * 32 + j % 32, height + cells[j]) );
			}
		}
	}

	// Each FastLZ level-one block contains the layer's heights, area IDs and connections.
	static void Expand( ReadOnlySpan<byte> source, Span<byte> output )
	{
		int pos = 0, written = 0;
		while ( pos < source.Length )
		{
			int control = source[pos++];
			if ( control < 32 )
			{
				int count = control + 1;
				if ( count > source.Length - pos || count > output.Length - written ) throw new InvalidDataException( "Invalid tile literals" );
				source.Slice( pos, count ).CopyTo( output[written..] );
				written += count;
				pos += count;
				continue;
			}
			int length = (control >> 5) + 2;
			if ( length == 9 && pos < source.Length ) length += source[pos++];
			if ( pos >= source.Length ) throw new InvalidDataException( "Truncated tile match" );
			int match = written - ((control & 31) << 8) - source[pos++] - 1;
			if ( match < 0 || length > output.Length - written ) throw new InvalidDataException( "Invalid tile match" );
			while ( length-- > 0 ) output[written++] = output[match++];
		}
		if ( written != output.Length ) throw new InvalidDataException( "Incomplete tile layer" );
	}
}
