using System;
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

	// Header field offsets for MccHalo2U1 (version 13)
	const int OffsetFlags = 0x01C;
	const int OffsetCompressedDataChunkSize = 0x308;
	const int OffsetCompressedChunkTableAddr = 0x310;
	const int OffsetCompressedChunkCount = 0x314;

	/// <summary>
	/// Returns a MemoryStream containing the fully decompressed map data.
	/// If the map is not compressed, returns the raw file contents in a MemoryStream.
	/// </summary>
	public static Stream Open( BaseFileSystem fileSystem, string mapFilePath )
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
		fs.Seek( OffsetFlags, SeekOrigin.Begin );
		var flags = reader.ReadInt16();
		bool isCompressed = (flags & 1) != 0;

		if ( !isCompressed )
		{
			fs.Seek( 0, SeekOrigin.Begin );
			var raw = new byte[fs.Length];
			fs.ReadExactly( raw, 0, raw.Length );
			return new MemoryStream( raw, writable: false );
		}

		// Read compression metadata
		fs.Seek( OffsetCompressedDataChunkSize, SeekOrigin.Begin );
		int chunkTargetSize = reader.ReadInt32();
		reader.ReadInt32(); // compressedDataOffset (not needed)
		int chunkTableAddr = reader.ReadInt32();
		int chunkCount = reader.ReadInt32();

		// Read header verbatim
		fs.Seek( 0, SeekOrigin.Begin );
		var headerBytes = reader.ReadBytes( HeaderSize );

		var output = new MemoryStream();
		output.Write( headerBytes );
		for ( int i = 0; i < chunkCount; i++ )
		{
			fs.Position = chunkTableAddr + 8L * i;
			int size = reader.ReadInt32();
			fs.Position = reader.ReadInt32();
			if ( size < 0 )
			{
				// Stored chunks have two unused header bytes.
				fs.Position += 2;
				var raw = new byte[-size];
				fs.ReadExactly( raw );
				output.Write( raw );
			}
			else
			{
				output.Write( ReadZlibChunk( fs, size, chunkTargetSize ) );
			}
		}

		output.Position = 0;
		return output;
	}

	// MCC stores zlib frames. DeflateStream reads the payload between the
	// two-byte zlib header and the big-endian Adler-32 trailer.
	internal static byte[] ReadZlibChunk( Stream source, int size, int capacity = 0 )
	{
		if ( size < 6 ) throw new InvalidDataException( "Truncated zlib chunk." );
		var frame = new byte[size];
		source.ReadExactly( frame );
		if ( (frame[0] & 15) != 8 || (frame[0] >> 4) > 7 || (frame[1] & 32) != 0 || (frame[0] * 256 + frame[1]) % 31 != 0 )
			throw new InvalidDataException( "Invalid zlib header." );

		using var payload = new MemoryStream( frame, 2, size - 6, writable: false );
		using var inflater = new DeflateStream( payload, CompressionMode.Decompress );
		using var output = new MemoryStream( capacity );
		inflater.CopyTo( output );
		var bytes = output.ToArray();
		uint a = 1, b = 0;
		foreach ( byte value in bytes )
		{
			a = (a + value) % 65521;
			b = (b + a) % 65521;
		}
		var expected = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian( frame.AsSpan( size - 4 ) );
		if ( ((b << 16) | a) != expected ) throw new InvalidDataException( "Invalid zlib checksum." );
		return bytes;
	}
}
