using System;
using System.IO;

namespace GoldSrc.Bsp;

/// <summary>
/// A decoded skybox side, always stored top-down as tightly packed RGBA.
/// </summary>
public sealed class SkyImage
{
	public int Width { get; init; }
	public int Height { get; init; }
	public byte[] Rgba { get; init; }

	/// <summary>
	/// Half-Life ships each skybox side as both a TGA and a BMP. The TGA is the higher quality
	/// one so we prefer it, but either will do.
	/// </summary>
	public static SkyImage Load( BaseFileSystem fileSystem, string filePath )
	{
		try
		{
			var data = fileSystem.ReadAllBytes( filePath ).ToArray();

			return Path.GetExtension( filePath ).ToLowerInvariant() switch
			{
				".tga" => ReadTga( data ),
				".bmp" => ReadBmp( data ),
				_ => null,
			};
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to read sky image {filePath}: {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Uncompressed and RLE true colour TGA, which covers everything in gfx/env.
	/// </summary>
	private static SkyImage ReadTga( byte[] data )
	{
		if ( data.Length < 18 )
			return null;

		var idLength = data[0];
		var imageType = data[2];
		var width = BitConverter.ToUInt16( data, 12 );
		var height = BitConverter.ToUInt16( data, 14 );
		var bpp = data[16];
		var descriptor = data[17];

		if ( width <= 0 || height <= 0 )
			return null;

		if ( imageType is not (2 or 10) || bpp is not (24 or 32) )
			return null;

		var bytesPerPixel = bpp / 8;
		var pixels = new byte[width * height * 4];
		var offset = 18 + idLength;

		// Bit 5 of the descriptor puts the origin in the top left, otherwise it's bottom left.
		var topDown = (descriptor & 0x20) != 0;

		void WritePixel( int index, byte b, byte g, byte r, byte a )
		{
			var x = index % width;
			var y = index / width;
			if ( y >= height )
				return;

			if ( !topDown )
				y = height - 1 - y;

			var dst = ((y * width) + x) * 4;
			pixels[dst + 0] = r;
			pixels[dst + 1] = g;
			pixels[dst + 2] = b;
			pixels[dst + 3] = a;
		}

		var total = width * height;

		if ( imageType == 2 )
		{
			if ( offset + (total * bytesPerPixel) > data.Length )
				return null;

			for ( var i = 0; i < total; ++i )
			{
				var src = offset + (i * bytesPerPixel);
				WritePixel( i, data[src], data[src + 1], data[src + 2], bytesPerPixel == 4 ? data[src + 3] : (byte)255 );
			}

			return new SkyImage { Width = width, Height = height, Rgba = pixels };
		}

		for ( var i = 0; i < total; )
		{
			if ( offset >= data.Length )
				break;

			var packet = data[offset++];
			var count = (packet & 0x7f) + 1;

			// High bit set means the following single pixel repeats "count" times.
			if ( (packet & 0x80) != 0 )
			{
				if ( offset + bytesPerPixel > data.Length )
					break;

				var b = data[offset];
				var g = data[offset + 1];
				var r = data[offset + 2];
				var a = bytesPerPixel == 4 ? data[offset + 3] : (byte)255;
				offset += bytesPerPixel;

				for ( var n = 0; n < count && i < total; ++n, ++i )
					WritePixel( i, b, g, r, a );

				continue;
			}

			for ( var n = 0; n < count && i < total; ++n, ++i )
			{
				if ( offset + bytesPerPixel > data.Length )
					break;

				WritePixel( i, data[offset], data[offset + 1], data[offset + 2], bytesPerPixel == 4 ? data[offset + 3] : (byte)255 );
				offset += bytesPerPixel;
			}
		}

		return new SkyImage { Width = width, Height = height, Rgba = pixels };
	}

	/// <summary>
	/// Bottom up BITMAPINFOHEADER bitmaps, 8 bit paletted or 24/32 bit, which is what the
	/// gfx/env BMPs are.
	/// </summary>
	private static SkyImage ReadBmp( byte[] data )
	{
		if ( data.Length < 54 || data[0] != 'B' || data[1] != 'M' )
			return null;

		var dataOffset = BitConverter.ToInt32( data, 10 );
		var headerSize = BitConverter.ToInt32( data, 14 );
		var width = BitConverter.ToInt32( data, 18 );
		var height = BitConverter.ToInt32( data, 22 );
		var bpp = BitConverter.ToUInt16( data, 28 );
		var compression = BitConverter.ToInt32( data, 30 );

		if ( width <= 0 || height == 0 || compression != 0 )
			return null;

		if ( bpp is not (8 or 24 or 32) )
			return null;

		var topDown = height < 0;
		height = Math.Abs( height );

		var palette = data.AsSpan( 14 + headerSize );
		var stride = ((width * bpp / 8) + 3) & ~3;
		var pixels = new byte[width * height * 4];

		for ( var y = 0; y < height; ++y )
		{
			var row = dataOffset + ((topDown ? y : height - 1 - y) * stride);
			if ( row + stride > data.Length )
				break;

			for ( var x = 0; x < width; ++x )
			{
				var dst = ((y * width) + x) * 4;

				if ( bpp == 8 )
				{
					var entry = data[row + x] * 4;
					if ( entry + 2 >= palette.Length )
						continue;

					pixels[dst + 0] = palette[entry + 2];
					pixels[dst + 1] = palette[entry + 1];
					pixels[dst + 2] = palette[entry + 0];
					pixels[dst + 3] = 255;
					continue;
				}

				var src = row + (x * bpp / 8);
				pixels[dst + 0] = data[src + 2];
				pixels[dst + 1] = data[src + 1];
				pixels[dst + 2] = data[src + 0];
				pixels[dst + 3] = 255;
			}
		}

		return new SkyImage { Width = width, Height = height, Rgba = pixels };
	}
}
