using System;

namespace Renderware;

/// <summary>
/// A texture dictionary (.txd). Parsing only keeps the texture headers; pixel data is read back from the source on
/// demand (with the file's bytes cached weakly), so mounting thousands of dictionaries stays cheap.
/// </summary>
public sealed class TxdFile
{
	public string Name { get; }
	public IReadOnlyList<TextureEntry> Textures { get; }

	/// <summary>Non-zero if this is a console dictionary we skipped.</summary>
	public uint UnsupportedPlatform { get; }

	readonly Func<Stream> open;
	readonly WeakReference<byte[]> bytes = new( null );

	/// <summary>
	/// A hash of each texture's header and pixel data (palette + top mip), so identical copies in different
	/// dictionaries can be told apart from textures that merely share a name.
	/// </summary>
	public IReadOnlyDictionary<TextureEntry, ulong> ContentHashes { get; }

	public TxdFile( string name, Func<Stream> open )
	{
		Name = name;
		this.open = open;

		var data = Bytes();
		using var stream = new MemoryStream( data, false );
		var dictionary = Chunks.ReadRoot<TextureDictionary>( stream );

		Textures = dictionary.Textures;
		UnsupportedPlatform = dictionary.UnsupportedPlatform;
		ContentHashes = Textures.ToDictionary( t => t, t => Hash( data, t ) );
	}

	/// <summary>
	/// FNV-1a over the texture's dimensions, format, palette and top mip.
	/// </summary>
	static ulong Hash( byte[] data, TextureEntry entry )
	{
		ulong hash = 14695981039346656037UL;
		void Mix( uint value )
		{
			for ( int i = 0; i < 4; i++, value >>= 8 )
				hash = (hash ^ (value & 0xFF)) * 1099511628211UL;
		}

		Mix( (uint)entry.Width );
		Mix( (uint)entry.Height );
		Mix( (uint)entry.Format );
		Mix( (uint)entry.Dxt );

		// palette (if any) + the top mip's size and pixels
		long start = ChunkHeader.Size + entry.DataOffset;
		int paletteBytes = entry.IsPalettized ? (entry.Format.HasFlag( TextureEntry.RasterFormat.Pal8 ) ? 256 : 16) * 4 : 0;
		long sizeAt = start + paletteBytes;
		if ( sizeAt + 4 > data.Length )
			return hash;

		long end = Math.Min( data.Length, sizeAt + 4 + BitConverter.ToUInt32( data, (int)sizeAt ) );
		for ( long i = start; i < end; i++ )
			hash = (hash ^ data[i]) * 1099511628211UL;

		return hash;
	}

	byte[] Bytes()
	{
		lock ( bytes )
		{
			if ( bytes.TryGetTarget( out var data ) )
				return data;

			using var stream = open();
			using var memory = new MemoryStream();
			stream.CopyTo( memory );
			data = memory.ToArray();
			bytes.SetTarget( data );
			return data;
		}
	}

	/// <summary>
	/// The pixel format a texture's data comes back in from <see cref="ReadMips"/> (palettes are expanded to RGBA).
	/// <see cref="ImageFormat.None"/> if we can't read it.
	/// </summary>
	public static ImageFormat GetFormat( TextureEntry entry )
	{
		if ( entry.IsPalettized )
			return ImageFormat.RGBA8888;

		switch ( entry.Dxt )
		{
			case TextureEntry.Compression.DXT1: return entry.HasAlpha ? ImageFormat.DXT1_ONEBITALPHA : ImageFormat.DXT1;
			case TextureEntry.Compression.DXT3: return ImageFormat.DXT3;
			case TextureEntry.Compression.DXT5: return ImageFormat.DXT5;
		}

		// D3D stores 32 bit rasters as B, G, R, A in memory
		return (entry.Format & TextureEntry.RasterFormat.Mask) switch
		{
			TextureEntry.RasterFormat.R565 => ImageFormat.BGR565,
			TextureEntry.RasterFormat.R1555 => ImageFormat.BGRA5551,
			TextureEntry.RasterFormat.R4444 => ImageFormat.BGRA4444,
			TextureEntry.RasterFormat.R8888 => ImageFormat.BGRA8888,
			TextureEntry.RasterFormat.R888 => ImageFormat.BGRA8888,
			TextureEntry.RasterFormat.Lum8 => ImageFormat.A8,
			_ => ImageFormat.None,
		};
	}

	/// <summary>
	/// Up to <paramref name="maxMips"/> mip levels of a texture, largest first, in <see cref="GetFormat"/>'s format.
	/// </summary>
	public byte[][] ReadMips( TextureEntry entry, int maxMips = int.MaxValue )
	{
		var data = Bytes();

		// entry offsets are relative to the dictionary chunk's contents, after its 12 byte header
		using var br = new BinaryReader( new MemoryStream( data, false ) );
		br.BaseStream.Position = ChunkHeader.Size + entry.DataOffset;

		byte[] palette = null;
		if ( entry.IsPalettized )
			palette = br.ReadBytes( (entry.Format.HasFlag( TextureEntry.RasterFormat.Pal8 ) ? 256 : 16) * 4 );

		var mips = new List<byte[]>();
		for ( int i = 0; i < Math.Min( entry.MipCount, maxMips ) && br.BaseStream.Length - br.BaseStream.Position >= 4; i++ )
		{
			var mip = br.ReadBytes( (int)br.ReadUInt32() );

			// San Andreas stores the smallest DXT mips of non-square textures as 0 bytes - end the chain there
			if ( mip.Length == 0 )
				break;

			mips.Add( palette is null ? mip : ExpandPalette( mip, palette ) );
		}

		return mips.ToArray();
	}

	/// <summary>
	/// Whether a texture needs alpha testing: its raster has alpha and some texels of the top mip are actually below
	/// half. (Both checks - some formats carry junk alpha, and plenty of "alpha" textures are fully opaque.)
	/// </summary>
	public bool HasCutoutAlpha( TextureEntry entry )
	{
		if ( !entry.HasAlpha || ReadMips( entry, 1 ) is not [var data, ..] )
			return false;

		switch ( GetFormat( entry ) )
		{
			case ImageFormat.DXT1:
			case ImageFormat.DXT1_ONEBITALPHA:
				// punch-through blocks (colour0 <= colour1) using index 3
				for ( int b = 0; b + 8 <= data.Length; b += 8 )
				{
					if ( (data[b] | (data[b + 1] << 8)) > (data[b + 2] | (data[b + 3] << 8)) )
						continue;

					uint bits = BitConverter.ToUInt32( data, b + 4 );
					for ( int i = 0; i < 16; i++ )
						if ( ((bits >> (i * 2)) & 3) == 3 ) return true;
				}
				return false;

			case ImageFormat.DXT3:
				for ( int b = 0; b + 16 <= data.Length; b += 16 )
					for ( int i = 0; i < 8; i++ )
						if ( (data[b + i] & 0x0F) < 8 || (data[b + i] >> 4) < 8 ) return true;
				return false;

			case ImageFormat.DXT5:
				for ( int b = 0; b + 16 <= data.Length; b += 16 )
					if ( Math.Min( data[b], data[b + 1] ) < 128 ) return true;
				return false;

			case ImageFormat.RGBA8888:
			case ImageFormat.BGRA8888:
				for ( int i = 3; i < data.Length; i += 4 )
					if ( data[i] < 128 ) return true;
				return false;

			case ImageFormat.BGRA5551:
				for ( int i = 1; i < data.Length; i += 2 )
					if ( (data[i] & 0x80) == 0 ) return true;
				return false;

			case ImageFormat.BGRA4444:
				for ( int i = 1; i < data.Length; i += 2 )
					if ( (data[i] >> 4) < 8 ) return true;
				return false;

			default:
				return false;
		}
	}

	/// <summary>
	/// Whether a texture's alpha is a soft fade (light beams, glows, glass) rather than on / off (foliage, fences):
	/// most of the top mip's texels sit between clear and solid. Foliage has soft, antialiased edges too - in San
	/// Andreas up to ~30% of texels - but it's mostly clear or solid. Soft alpha wants blending, on / off alpha is
	/// better alpha tested - it keeps depth and sorts for free.
	/// </summary>
	public bool HasSoftAlpha( TextureEntry entry )
	{
		if ( !entry.HasAlpha || ReadMips( entry, 1 ) is not [var data, ..] )
			return false;

		int soft = 0, total = 0;
		void Count( int alpha )
		{
			total++;
			if ( alpha > 24 && alpha < 232 ) soft++;
		}

		switch ( GetFormat( entry ) )
		{
			case ImageFormat.RGBA8888:
			case ImageFormat.BGRA8888:
				for ( int i = 3; i < data.Length; i += 4 ) Count( data[i] );
				break;

			case ImageFormat.BGRA4444:
				for ( int i = 1; i < data.Length; i += 2 ) Count( (data[i] >> 4) * 17 );
				break;

			case ImageFormat.DXT3:
				for ( int b = 0; b + 16 <= data.Length; b += 16 )
					for ( int i = 0; i < 8; i++ )
					{
						Count( (data[b + i] & 0x0F) * 17 );
						Count( (data[b + i] >> 4) * 17 );
					}
				break;

			case ImageFormat.DXT5:
				// close enough: a block whose alpha endpoints aren't just clear / solid has in-between texels
				for ( int b = 0; b + 16 <= data.Length; b += 16 )
				{
					Count( data[b] );
					Count( data[b + 1] );
				}
				break;

			default:
				return false; // DXT1 / 1555 alpha is one bit
		}

		return total > 0 && soft * 2 > total;
	}

	static byte[] ExpandPalette( byte[] indices, byte[] palette )
	{
		var rgba = new byte[indices.Length * 4];
		for ( int i = 0; i < indices.Length; i++ )
			Buffer.BlockCopy( palette, (indices[i] * 4) % palette.Length, rgba, i * 4, 4 );
		return rgba;
	}
}
