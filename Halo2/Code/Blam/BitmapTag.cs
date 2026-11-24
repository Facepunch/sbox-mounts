using System;
using System.IO;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Halo2.Blam;

/// <summary>
/// Parses a Halo 2 bitmap (bitm) tag.
/// References: Gravemind2401/Reclaimer - Halo2/bitmap.cs, DataPointer.cs
/// </summary>
public static class BitmapTag
{
	public static BitmapData Read( MapFile map, TagEntry tag, int index = 0 )
	{
		if ( tag.DataSize < 76 ) return null;
		int block = map.VaToOffset( (int)tag.DataVirtualAddress ) + 68;
		int count = map.ReadInt32( block );
		if ( (uint)index >= count ) return null;

		int offset = map.VaToOffset( map.ReadInt32( block + 4 ) ) + index * 168;
		using var r = map.OpenReader( offset, 168 );
		r.BaseStream.Position = 4;
		var bitmap = new BitmapData
		{
			Width = r.ReadInt16(), Height = r.ReadInt16(), Depth = r.ReadInt16(),
			BitmapType = r.ReadInt16(), Format = r.ReadInt16(), Flags = r.ReadInt16()
		};
		r.BaseStream.Position = 20;
		bitmap.MipmapCount = r.ReadInt16();
		r.BaseStream.Position = 56;
		bitmap.Lod0Pointer = r.ReadInt32();
		r.BaseStream.Position = 80;
		bitmap.Lod0Size = r.ReadInt32();
		return bitmap;
	}

	// Staging buffers contain compressed bytes. Uploaded pixels
	// have a separate lifetime. Concurrent loads rent independent buffers.
	static readonly ArrayPool<byte> Staging = ArrayPool<byte>.Create( 4 * 1024 * 1024, 2 );

	public static byte[] ReadPixelData( BaseFileSystem fileSystem, MapFile map, BitmapData bmp, ReadOnlySpan<int> mipSizes, int length )
	{
		using var fs = fileSystem.OpenRead( $"{map.MapsDirectory}/textures.dat" );
		fs.Position = bmp.Lod0Pointer & 0x3fffffff;
		if ( bmp.Lod0Size < 4 || bmp.Lod0Size > fs.Length - fs.Position )
			throw new InvalidDataException( "Invalid bitmap resource size." );

		var packed = Staging.Rent( bmp.Lod0Size );
		try
		{
			fs.ReadExactly( packed, 0, bmp.Lod0Size );
			int count = BinaryPrimitives.ReadInt32LittleEndian( packed );
			if ( count <= 0 || count > (bmp.Lod0Size - 4) / 4 )
				throw new InvalidDataException( "Invalid bitmap segment table." );

			var pixels = new byte[length];
			int level = 0, written = 0, end = length;
			int cursor = 4 + count * 4;
			for ( int i = 0; i < count && level < mipSizes.Length; i++ )
			{
				int size = BinaryPrimitives.ReadInt32LittleEndian( packed.AsSpan( 4 + i * 4 ) );
				int sizeBytes = Math.Abs( size );
				if ( sizeBytes > bmp.Lod0Size - cursor ) throw new InvalidDataException( "Truncated bitmap segment." );
				using var payload = size > 0 ? MapDecompressor.OpenDeflatePayload( packed, cursor, size ) : null;
				using var inflater = payload is null ? null : new DeflateStream( payload, CompressionMode.Decompress );
				int remaining = sizeBytes;
				uint a = 1, b = 0;
				while ( level < mipSizes.Length )
				{
					int start = end - mipSizes[level];
					var destination = pixels.AsSpan( start + written, mipSizes[level] - written );
					int n;
					if ( inflater is null )
					{
						n = Math.Min( remaining, destination.Length );
						packed.AsSpan( cursor + sizeBytes - remaining, n ).CopyTo( destination );
						remaining -= n;
					}
					else
					{
						n = inflater.Read( destination );
						MapDecompressor.UpdateChecksum( destination[..n], ref a, ref b );
					}
					if ( n == 0 ) break;
					written += n;
					if ( written == mipSizes[level] )
					{
						written = 0;
						end = start;
						level++;
					}
				}
				if ( inflater is not null )
				{
					if ( level == mipSizes.Length && inflater.ReadByte() != -1 )
						throw new InvalidDataException( "Unexpected bitmap data after final mip." );
					MapDecompressor.CheckChecksum( a, b, packed.AsSpan( cursor + size - 4 ) );
				}
				cursor += sizeBytes;
			}
			if ( level != mipSizes.Length ) throw new InvalidDataException( "Incomplete bitmap mip chain." );
			return pixels;
		}
		finally
		{
			Staging.Return( packed );
		}
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
		public const int U8V8 = 22;
		public const int G8B8 = 23;
	}
}
