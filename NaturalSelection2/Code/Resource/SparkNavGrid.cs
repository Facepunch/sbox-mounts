using System;
using System.Buffers.Binary;
using System.IO;

namespace NS2;

/// <summary>Layers in a prebuilt Spark Detour navigation grid, in mounted world axes.</summary>
internal static class SparkNavGrid
{
	internal readonly record struct Layer( int X, int Y, int Width, int Height, int BaseHeight, Vector3 Min, Vector3 Max );
	internal delegate void Visit( Layer layer, ReadOnlySpan<byte> heights, ReadOnlySpan<byte> areas, ReadOnlySpan<byte> connections );

	internal static void Read( byte[] data, Visit visit )
	{
		using var reader = new BinaryReader( new MemoryStream( data ) );
		reader.BaseStream.Position = 16;
		int slots = checked(reader.ReadInt32() * reader.ReadInt32() * reader.ReadInt32());
		if ( slots < 0 || slots > (data.Length - 28) / 4 ) throw new InvalidDataException( "Invalid tile count" );
		byte[] buffer = [];
		for ( int i = 0; i < slots; i++ )
		{
			int size = reader.ReadInt32();
			if ( size == 0 ) continue;
			int offset = (int)reader.BaseStream.Position;
			if ( size < 56 || size > data.Length - offset ) throw new InvalidDataException( "Invalid tile size" );
			var bytes = data.AsSpan( offset, size );
			reader.BaseStream.Position += size;
			if ( !bytes[..4].SequenceEqual( "RLTD"u8 ) || BinaryPrimitives.ReadInt32LittleEndian( bytes[4..] ) != 1 )
				throw new InvalidDataException( "Unsupported tile layer" );

			int width = bytes[49], height = bytes[48], count = width * height;
			if ( count == 0 ) throw new InvalidDataException( "Empty tile layer" );
			if ( buffer.Length < count * 3 ) buffer = new byte[count * 3];
			Expand( bytes[56..], buffer.AsSpan( 0, count * 3 ) );
			var layer = new Layer( BitConverter.ToInt32( bytes[12..] ), BitConverter.ToInt32( bytes[8..] ),
				width, height, BitConverter.ToUInt16( bytes[44..] ), Point( bytes[20..] ), Point( bytes[32..] ) );
			visit( layer, buffer.AsSpan( 0, count ), buffer.AsSpan( count, count ), buffer.AsSpan( 2 * count, count ) );
		}
	}

	static Vector3 Point( ReadOnlySpan<byte> bytes ) =>
		new Vector3( BitConverter.ToSingle( bytes[8..] ), BitConverter.ToSingle( bytes ), BitConverter.ToSingle( bytes[4..] ) ) * MapLoader.Scale;

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
