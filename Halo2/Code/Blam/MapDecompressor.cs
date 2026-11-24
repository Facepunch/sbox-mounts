using System;
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
		var frame = Array.Empty<byte>();
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
				InflateChunk( fs, size, output, ref frame );
			}
		}

		if ( output.Position != fileSize ) throw new InvalidDataException( "Incorrect decompressed map size." );
		return data;
	}

	// MCC stores zlib frames. DeflateStream reads the payload between the
	// two-byte zlib header and the big-endian Adler-32 trailer.
	internal static void InflateChunk( Stream source, int size, MemoryStream output, ref byte[] frame )
	{
		if ( size < 6 || size > source.Length - source.Position ) throw new InvalidDataException( "Truncated zlib chunk." );
		if ( frame.Length < size ) frame = new byte[size];
		source.ReadExactly( frame, 0, size );
		if ( (frame[0] & 15) != 8 || (frame[0] >> 4) > 7 || (frame[1] & 32) != 0 || (frame[0] * 256 + frame[1]) % 31 != 0 )
			throw new InvalidDataException( "Invalid zlib header." );

		using var payload = new MemoryStream( frame, 2, size - 6, writable: false );
		using var inflater = new DeflateStream( payload, CompressionMode.Decompress );
		int start = (int)output.Position;
		inflater.CopyTo( output );
		var bytes = output.GetBuffer().AsSpan( start, (int)output.Position - start );
		uint a = 1, b = 0;
		// Adler-32 can accumulate up to 5552 bytes before either sum overflows.
		while ( !bytes.IsEmpty )
		{
			int count = Math.Min( bytes.Length, 5552 );
			foreach ( byte value in bytes[..count] )
			{
				a += value;
				b += a;
			}
			a %= 65521;
			b %= 65521;
			bytes = bytes[count..];
		}
		var expected = BinaryPrimitives.ReadUInt32BigEndian( frame.AsSpan( size - 4 ) );
		if ( ((b << 16) | a) != expected ) throw new InvalidDataException( "Invalid zlib checksum." );
	}
}
