using System;
using System.Collections.Generic;
using System.IO;

namespace Halo2.Blam;

/// <summary>
/// Parses a Halo 2 bitmap (bitm) tag.
/// References: Gravemind2401/Reclaimer - Halo2/bitmap.cs, DataPointer.cs
/// </summary>
public class BitmapTag
{
	public List<BitmapData> Bitmaps { get; } = new();

	public BitmapTag( MapFile map, TagEntry tag )
	{
		if ( tag.DataSize == 0 )
			return;

		using var reader = map.GetTagDataReader( tag );

		// For MCC (Halo2Xbox+): Bitmaps TagBlock at +68
		reader.BaseStream.Seek( 68, SeekOrigin.Begin );
		int bitmapCount = reader.ReadInt32();
		int bitmapPtr = reader.ReadInt32();

		if ( bitmapCount <= 0 || bitmapPtr == 0 )
			return;

		int baseOff = map.VaToOffset( bitmapPtr );

		// BitmapDataBlock is 168 bytes for MCC
		for ( int i = 0; i < bitmapCount; i++ )
		{
			int off = baseOff + i * 168;
			using var r = map.OpenReader( off, 168 );

			r.ReadInt32(); // +0: "bitm" class tag

			var bmp = new BitmapData
			{
				Width = r.ReadInt16(),        // +4
				Height = r.ReadInt16(),       // +6
				Depth = r.ReadInt16(),        // +8
				BitmapType = r.ReadInt16(),   // +10
				Format = r.ReadInt16(),       // +12
				Flags = r.ReadInt16(),        // +14
				RegX = r.ReadInt16(),         // +16
				RegY = r.ReadInt16(),         // +18
				MipmapCount = r.ReadInt16()   // +20
			};

			// Lod0Pointer at +56 (for MCC)
			r.BaseStream.Seek( 56, SeekOrigin.Begin );
			bmp.Lod0Pointer = r.ReadInt32();

			// Lod0Size at +80
			r.BaseStream.Seek( 80, SeekOrigin.Begin );
			bmp.Lod0Size = r.ReadInt32();

			Bitmaps.Add( bmp );
		}
	}

	/// <summary>
	/// Reads pixel data for a bitmap. MCC stores bitmaps in textures.dat with compressed segments.
	/// </summary>
	public static byte[] ReadPixelData( BaseFileSystem fileSystem, MapFile map, BitmapData bmp, int maxBytes = int.MaxValue )
	{
		if ( bmp.Lod0Size <= 0 )
			return null;

		int address = bmp.Lod0Pointer & 0x3FFFFFFF;

		// MCC bitmaps are in textures.dat
		var texturesDatPath = $"{map.MapsDirectory}/textures.dat";
		if ( !fileSystem.FileExists( texturesDatPath ) )
		{
			// Try parent directory
			var parentDir = Path.GetDirectoryName( map.MapsDirectory )?.Replace( '\\', '/' ) ?? "";
			texturesDatPath = $"{parentDir}/halo2/h2_maps_win64_dx11/textures.dat";
			if ( !fileSystem.FileExists( texturesDatPath ) )
				return null;
		}

		using var fs = fileSystem.OpenRead( texturesDatPath );
		fs.Seek( address, SeekOrigin.Begin );

		using var br = new BinaryReader( fs );
		int segmentCount = br.ReadInt32();
		var segments = new int[segmentCount];
		for ( int i = 0; i < segmentCount; i++ )
			segments[i] = br.ReadInt32();

		return DecompressSegments( fs, segments, maxBytes );
	}

	static byte[] DecompressSegments( Stream source, int[] segments, int maxBytes )
	{
		using var output = new MemoryStream();
		var frame = Array.Empty<byte>();

		foreach ( int blockSize in segments )
		{
			if ( output.Length >= maxBytes ) break;

			if ( blockSize < 0 )
			{
				// Negative = stored raw (uncompressed)
				int actualSize = Math.Min( checked(-blockSize), maxBytes - (int)output.Length );
				var buffer = new byte[actualSize];
				source.ReadExactly( buffer, 0, actualSize );
				output.Write( buffer, 0, actualSize );
			}
			else
			{
				// Positive = ZLib compressed
				MapDecompressor.InflateChunk( source, blockSize, output, ref frame );
			}
		}

		if ( output.Length > maxBytes ) output.SetLength( maxBytes );
		return output.ToArray();
	}
}

public class BitmapData
{
	public short Width { get; set; }
	public short Height { get; set; }
	public short Depth { get; set; }
	public short BitmapType { get; set; }
	public short Format { get; set; }
	public short Flags { get; set; }
	public short RegX { get; set; }
	public short RegY { get; set; }
	public short MipmapCount { get; set; }
	public int Lod0Pointer { get; set; }
	public int Lod0Size { get; set; }

	public bool IsSwizzled => (Flags & 8) != 0;

	/// <summary>
	/// Halo 2 texture format enum (matches Reclaimer TextureFormat).
	/// </summary>
	public static class TextureFormats
	{
		public const int A8 = 0;
		public const int Y8 = 1;
		public const int AY8 = 2;
		public const int A8Y8 = 3;
		public const int R5G6B5 = 6;
		public const int A1R5G5B5 = 8;
		public const int A4R4G4B4 = 9;
		public const int X8R8G8B8 = 10;
		public const int A8R8G8B8 = 11;
		public const int DXT1 = 14;
		public const int DXT3 = 15;
		public const int DXT5 = 16;
		public const int P8Bump = 17;
		public const int P8 = 18;
	}
}
