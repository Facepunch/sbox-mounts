using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Halo2.Blam;

/// <summary>
/// Decompresses Halo 2 MCC map files which use ZLib chunk-based compression.
/// The header (896 bytes) is always uncompressed. Remaining data is split into
/// ZLib-compressed chunks described by a chunk table in the header.
/// </summary>
public static class MapDecompressor
{
	const int HeaderSize = 896;

	/// <summary>Reads the uncompressed contents of a version 13 MCC cache.</summary>
	public static byte[] Read( BaseFileSystem fileSystem, string mapFilePath )
	{
		using var fs = fileSystem.OpenRead( mapFilePath );
		using var reader = new BinaryReader( fs );

		// Verify header
		var magic = reader.ReadUInt32();
		if ( magic != 0x68656164u ) // "daeh" LE
			throw new InvalidDataException( $"Not a Halo 2 map file (magic: 0x{magic:X8})" );

		var version = reader.ReadInt32();
		if ( version != 13 )
			throw new InvalidDataException( $"Unexpected Halo 2 map version: {version}" );

		// Check compressed flag (bit 0 of int16 at offset 0x1C)
		int fileSize = reader.ReadInt32();
		fs.Position = 0x1C;
		var flags = reader.ReadInt16();
		bool isCompressed = (flags & 1) != 0;

		if ( !isCompressed )
		{
			fs.Seek( 0, SeekOrigin.Begin );
			var raw = new byte[fs.Length];
			fs.ReadExactly( raw, 0, raw.Length );
			return raw;
		}

		fs.Position = 0x310;
		int tableOffset = reader.ReadInt32();
		int chunkCount = reader.ReadInt32();
		if ( fileSize < HeaderSize || chunkCount < 0 || tableOffset < HeaderSize || tableOffset + 8L * chunkCount > fs.Length )
			throw new InvalidDataException( "Invalid map chunk table." );

		var data = new byte[fileSize];
		fs.Position = 0;
		fs.ReadExactly( data, 0, HeaderSize );
		using var output = new MemoryStream( data, 0, data.Length, writable: true, publiclyVisible: true );
		output.Position = HeaderSize;
		fs.Position = tableOffset;
		var table = new byte[checked(chunkCount * 8)];
		fs.ReadExactly( table );
		for ( int i = 0; i < chunkCount; i++ )
		{
			int size = BinaryPrimitives.ReadInt32LittleEndian( table.AsSpan( i * 8 ) );
			fs.Position = BinaryPrimitives.ReadInt32LittleEndian( table.AsSpan( i * 8 + 4 ) );
			if ( size < 0 )
			{
				// Stored chunks have two unused header bytes.
				fs.Position += 2;
				fs.ReadExactly( data, (int)output.Position, checked(-size) );
				output.Position -= size;
			}
			else
			{
				InflateChunk( fs, size, output );
			}
		}

		if ( output.Position != fileSize ) throw new InvalidDataException( "Incorrect decompressed map size." );
		return data;
	}

	// MCC stores zlib frames. DeflateStream reads the payload between the
	// two-byte zlib header and the big-endian Adler-32 trailer.
	static void InflateChunk( Stream source, int size, MemoryStream output )
	{
		if ( size < 6 || size > source.Length - source.Position ) throw new InvalidDataException( "Truncated zlib chunk." );
		var frame = ArrayPool<byte>.Shared.Rent( size );
		try
		{
			source.ReadExactly( frame, 0, size );
			using var payload = OpenDeflatePayload( frame, 0, size );
			using var inflater = new DeflateStream( payload, CompressionMode.Decompress );
			int start = (int)output.Position;
			inflater.CopyTo( output );
			CheckChecksum( output.GetBuffer().AsSpan( start, (int)output.Position - start ), frame.AsSpan( size - 4 ) );
		}
		finally
		{
			ArrayPool<byte>.Shared.Return( frame );
		}
	}

	internal static MemoryStream OpenDeflatePayload( byte[] frame, int start, int size )
	{
		if ( size < 6 || (frame[start] & 15) != 8 || (frame[start] >> 4) > 7 || (frame[start + 1] & 32) != 0
			|| (frame[start] * 256 + frame[start + 1]) % 31 != 0 )
			throw new InvalidDataException( "Invalid zlib header." );
		return new MemoryStream( frame, start + 2, size - 6, writable: false );
	}

	internal static void CheckChecksum( ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> trailer )
	{
		uint a = 1, b = 0;
		UpdateChecksum( bytes, ref a, ref b );
		CheckChecksum( a, b, trailer );
	}

	internal static void UpdateChecksum( ReadOnlySpan<byte> bytes, ref uint a, ref uint b )
	{
		// Adler-32 can accumulate up to 5552 bytes before either sum overflows.
		while ( !bytes.IsEmpty )
		{
			int count = Math.Min( bytes.Length, 5552 );
			int i = 0;
			// Four Adler steps with one dependency on each running sum.
			for ( ; i + 4 <= count; i += 4 )
			{
				uint x = bytes[i], y = bytes[i + 1], z = bytes[i + 2], w = bytes[i + 3];
				b += 4 * (a + x) + 3 * y + 2 * z + w;
				a += x + y + z + w;
			}
			foreach ( byte value in bytes[i..count] )
			{
				a += value;
				b += a;
			}
			a %= 65521;
			b %= 65521;
			bytes = bytes[count..];
		}
	}

	internal static void CheckChecksum( uint a, uint b, ReadOnlySpan<byte> trailer )
	{
		var expected = BinaryPrimitives.ReadUInt32BigEndian( trailer );
		if ( ((b << 16) | a) != expected ) throw new InvalidDataException( "Invalid zlib checksum." );
	}
}
