namespace IdTech3.Formats;

public enum PixelFormat { Rgba8, Dxt1, Dxt3, Dxt5 }

/// <summary>
/// A decoded image. For <see cref="PixelFormat.Rgba8"/> only the top mip is stored.
/// Compressed images keep their mip chain, largest first.
/// </summary>
public sealed class ImageData
{
	public int Width, Height;
	public PixelFormat Format = PixelFormat.Rgba8;
	public List<byte[]> Mips = new();
	public bool HasAlpha;

	public byte[] Pixels => Mips.Count > 0 ? Mips[0] : null;

	public static int MipSize( PixelFormat f, int w, int h )
	{
		w = Math.Max( 1, w ); h = Math.Max( 1, h );
		return f switch
		{
			PixelFormat.Rgba8 => w * h * 4,
			PixelFormat.Dxt1 => Math.Max( 1, (w + 3) / 4 ) * Math.Max( 1, (h + 3) / 4 ) * 8,
			_ => Math.Max( 1, (w + 3) / 4 ) * Math.Max( 1, (h + 3) / 4 ) * 16,
		};
	}

	/// <summary>Full chain down to 1x1 present?</summary>
	public bool HasFullMipChain
	{
		get
		{
			int levels = 1, w = Width, h = Height;
			while ( w > 1 || h > 1 ) { w = Math.Max( 1, w / 2 ); h = Math.Max( 1, h / 2 ); levels++; }
			return Mips.Count >= levels;
		}
	}

	/// <summary>Convert to RGBA8 (top mip only).</summary>
	public ImageData ToRgba()
	{
		if ( Format == PixelFormat.Rgba8 ) return this;
		var rgba = Dxt.Decode( Format, Mips[0], Width, Height );
		return new ImageData { Width = Width, Height = Height, Format = PixelFormat.Rgba8, Mips = { rgba }, HasAlpha = HasAlpha };
	}

	public static bool DetectAlpha( byte[] rgba )
	{
		for ( int i = 3; i < rgba.Length; i += 4 )
			if ( rgba[i] != 255 ) return true;
		return false;
	}
}

public static class Tga
{
	/// <summary>Decode any common TGA (uncompressed/RLE, true-color/grayscale/color-mapped) to top-down RGBA.</summary>
	public static ImageData Decode( byte[] data )
	{
		if ( data is null || data.Length < 18 ) return null;
		var b = new Bin( data );
		int idLength = b.U8( 0 );
		int colorMapType = b.U8( 1 );
		int imageType = b.U8( 2 );
		int cmFirst = b.U16( 3 ), cmLength = b.U16( 5 ), cmBits = b.U8( 7 );
		int width = b.U16( 12 ), height = b.U16( 14 );
		int bpp = b.U8( 16 );
		int desc = b.U8( 17 );

		if ( width <= 0 || height <= 0 || width > 16384 || height > 16384 ) return null;

		bool rle = imageType >= 9;
		int baseType = rle ? imageType - 8 : imageType;
		if ( baseType is not (1 or 2 or 3) ) return null;

		int pos = 18 + idLength;
		byte[] palette = null;
		if ( colorMapType == 1 )
		{
			int entryBytes = (cmBits + 7) / 8;
			palette = new byte[(cmFirst + cmLength) * 4];
			for ( int i = 0; i < cmLength; i++ )
			{
				ReadPixel( data, pos + i * entryBytes, cmBits, palette, (cmFirst + i) * 4 );
			}
			pos += cmLength * entryBytes;
		}

		int pixelBytes = (bpp + 7) / 8;
		if ( pixelBytes < 1 || pixelBytes > 4 ) return null;

		var outp = new byte[width * height * 4];
		int count = width * height;
		var tmp = new byte[4];
		int n = 0;

		void Emit( int srcPos )
		{
			int dst = n * 4;
			if ( baseType == 1 )
			{
				int idx = pixelBytes == 1 ? data[srcPos] : data[srcPos] | (data[srcPos + 1] << 8);
				if ( palette is not null && idx * 4 + 3 < palette.Length )
				{
					outp[dst] = palette[idx * 4]; outp[dst + 1] = palette[idx * 4 + 1];
					outp[dst + 2] = palette[idx * 4 + 2]; outp[dst + 3] = palette[idx * 4 + 3];
				}
				else outp[dst + 3] = 255;
			}
			else if ( baseType == 3 )
			{
				byte g = data[srcPos];
				outp[dst] = g; outp[dst + 1] = g; outp[dst + 2] = g;
				outp[dst + 3] = pixelBytes >= 2 ? data[srcPos + 1] : (byte)255;
			}
			else ReadPixel( data, srcPos, bpp, outp, dst );
			n++;
		}

		try
		{
			if ( !rle )
			{
				if ( pos + count * pixelBytes > data.Length ) count = Math.Max( 0, (data.Length - pos) / pixelBytes );
				for ( int i = 0; i < count; i++ ) Emit( pos + i * pixelBytes );
			}
			else
			{
				while ( n < count && pos < data.Length )
				{
					int header = data[pos++];
					int run = (header & 0x7f) + 1;
					if ( (header & 0x80) != 0 )
					{
						if ( pos + pixelBytes > data.Length ) break;
						for ( int i = 0; i < run && n < count; i++ ) Emit( pos );
						pos += pixelBytes;
					}
					else
					{
						for ( int i = 0; i < run && n < count; i++ )
						{
							if ( pos + pixelBytes > data.Length ) break;
							Emit( pos );
							pos += pixelBytes;
						}
					}
				}
			}
		}
		catch ( IndexOutOfRangeException )
		{
			// truncated file - keep what we have
		}

		// origin: bit 5 set = top-left, otherwise bottom-left
		if ( (desc & 0x20) == 0 )
		{
			int row = width * 4;
			var line = new byte[row];
			for ( int y = 0; y < height / 2; y++ )
			{
				int a = y * row, c = (height - 1 - y) * row;
				Buffer.BlockCopy( outp, a, line, 0, row );
				Buffer.BlockCopy( outp, c, outp, a, row );
				Buffer.BlockCopy( line, 0, outp, c, row );
			}
		}
		if ( (desc & 0x10) != 0 )
		{
			for ( int y = 0; y < height; y++ )
				for ( int x = 0; x < width / 2; x++ )
					for ( int k = 0; k < 4; k++ )
					{
						int a = (y * width + x) * 4 + k, c = (y * width + width - 1 - x) * 4 + k;
						(outp[a], outp[c]) = (outp[c], outp[a]);
					}
		}

		return new ImageData { Width = width, Height = height, Mips = { outp }, HasAlpha = ImageData.DetectAlpha( outp ) };
	}

	static void ReadPixel( byte[] src, int p, int bits, byte[] dst, int d )
	{
		switch ( bits )
		{
			case 32:
				dst[d] = src[p + 2]; dst[d + 1] = src[p + 1]; dst[d + 2] = src[p]; dst[d + 3] = src[p + 3];
				break;
			case 24:
				dst[d] = src[p + 2]; dst[d + 1] = src[p + 1]; dst[d + 2] = src[p]; dst[d + 3] = 255;
				break;
			case 15:
			case 16:
			{
				int v = src[p] | (src[p + 1] << 8);
				dst[d] = (byte)(((v >> 10) & 31) * 255 / 31);
				dst[d + 1] = (byte)(((v >> 5) & 31) * 255 / 31);
				dst[d + 2] = (byte)((v & 31) * 255 / 31);
				dst[d + 3] = 255;
				break;
			}
			case 8:
				dst[d] = dst[d + 1] = dst[d + 2] = src[p]; dst[d + 3] = 255;
				break;
		}
	}
}

public static class Dds
{
	public static ImageData Decode( byte[] data )
	{
		var b = new Bin( data );
		if ( data is null || data.Length < 128 || b.U32( 0 ) != 0x20534444 ) return null; // "DDS "

		int height = b.I32( 12 );
		int width = b.I32( 16 );
		int mipCount = Math.Max( 1, b.I32( 28 ) );
		int pfFlags = b.I32( 80 );
		uint fourCC = b.U32( 84 );
		int rgbBits = b.I32( 88 );
		uint rMask = b.U32( 92 ), gMask = b.U32( 96 ), bMask = b.U32( 100 ), aMask = b.U32( 104 );
		int caps2 = b.I32( 112 );
		if ( width <= 0 || height <= 0 ) return null;
		if ( (caps2 & 0x200) != 0 ) return null; // cubemaps not handled here

		int offset = 128;
		var img = new ImageData { Width = width, Height = height };

		if ( (pfFlags & 0x4) != 0 )
		{
			img.Format = fourCC switch
			{
				0x31545844 => PixelFormat.Dxt1, // DXT1
				0x32545844 or 0x33545844 => PixelFormat.Dxt3, // DXT2/3
				0x34545844 or 0x35545844 => PixelFormat.Dxt5, // DXT4/5
				_ => (PixelFormat)(-1),
			};
			if ( (int)img.Format < 0 ) return null;

			int w = width, h = height;
			for ( int i = 0; i < mipCount; i++ )
			{
				int size = ImageData.MipSize( img.Format, w, h );
				if ( !b.Has( offset, size ) ) break;
				img.Mips.Add( b.Slice( offset, size ) );
				offset += size;
				w = Math.Max( 1, w / 2 ); h = Math.Max( 1, h / 2 );
			}
			if ( img.Mips.Count == 0 ) return null;
			img.HasAlpha = img.Format != PixelFormat.Dxt1 || Dxt1HasAlpha( img.Mips[0] );
			return img;
		}

		// uncompressed
		int bytes = rgbBits / 8;
		if ( bytes < 1 || bytes > 4 ) return null;
		var outp = new byte[width * height * 4];
		bool alpha = (pfFlags & 0x1) != 0 && aMask != 0;
		bool lum = (pfFlags & 0x20000) != 0;
		for ( int i = 0; i < width * height; i++ )
		{
			int p = offset + i * bytes;
			if ( !b.Has( p, bytes ) ) break;
			uint v = 0;
			for ( int k = 0; k < bytes; k++ ) v |= (uint)data[p + k] << (8 * k);
			if ( lum )
			{
				byte l = (byte)Extract( v, rMask == 0 ? 0xff : rMask );
				outp[i * 4] = outp[i * 4 + 1] = outp[i * 4 + 2] = l;
			}
			else
			{
				outp[i * 4] = (byte)Extract( v, rMask );
				outp[i * 4 + 1] = (byte)Extract( v, gMask );
				outp[i * 4 + 2] = (byte)Extract( v, bMask );
			}
			outp[i * 4 + 3] = alpha ? (byte)Extract( v, aMask ) : (byte)255;
		}
		img.Format = PixelFormat.Rgba8;
		img.Mips.Add( outp );
		img.HasAlpha = alpha && ImageData.DetectAlpha( outp );
		return img;
	}

	static uint Extract( uint v, uint mask )
	{
		if ( mask == 0 ) return 0;
		int shift = 0;
		while ( ((mask >> shift) & 1) == 0 ) shift++;
		uint m = mask >> shift;
		uint x = (v & mask) >> shift;
		return m == 255 ? x : (uint)(x * 255 / m);
	}

	static bool Dxt1HasAlpha( byte[] blocks )
	{
		for ( int i = 0; i + 8 <= blocks.Length; i += 8 )
		{
			int c0 = blocks[i] | (blocks[i + 1] << 8);
			int c1 = blocks[i + 2] | (blocks[i + 3] << 8);
			if ( c0 > c1 ) continue;
			uint bits = (uint)(blocks[i + 4] | (blocks[i + 5] << 8) | (blocks[i + 6] << 16) | (blocks[i + 7] << 24));
			for ( int k = 0; k < 16; k++ )
				if ( ((bits >> (k * 2)) & 3) == 3 ) return true;
		}
		return false;
	}
}

/// <summary>
/// Infinity Ward image (CoD2 v5, CoD4/5 v6). Mips are stored smallest first after the header.
/// </summary>
public static class Iwi
{
	public static ImageData Decode( byte[] data )
	{
		var b = new Bin( data );
		if ( data is null || data.Length < 16 || data[0] != 'I' || data[1] != 'W' || data[2] != 'i' ) return null;
		int version = data[3];
		if ( version is not (5 or 6) ) return null;

		int format = data[4];
		int width = b.U16( 6 ), height = b.U16( 8 );
		var offsets = new int[4];
		for ( int i = 0; i < 4; i++ ) offsets[i] = b.I32( 12 + i * 4 );
		int headerEnd = 28;

		var pf = format switch
		{
			0x0B => PixelFormat.Dxt1,
			0x0C => PixelFormat.Dxt3,
			0x0D => PixelFormat.Dxt5,
			_ => PixelFormat.Rgba8,
		};

		// largest mip runs from offsets[0] to end of file
		int start = offsets[0] > headerEnd && offsets[0] < data.Length ? offsets[0] : headerEnd;
		int expected = format switch
		{
			0x01 => width * height * 4,
			0x02 => width * height * 3,
			0x03 => width * height * 2,
			0x04 => width * height,
			_ => ImageData.MipSize( pf, width, height ),
		};
		if ( start + expected > data.Length ) start = data.Length - expected;
		if ( start < headerEnd ) return null;

		var img = new ImageData { Width = width, Height = height, Format = pf };
		if ( pf != PixelFormat.Rgba8 )
		{
			img.Mips.Add( b.Slice( start, expected ) );
			img.HasAlpha = pf != PixelFormat.Dxt1;
			return img;
		}

		var outp = new byte[width * height * 4];
		for ( int i = 0; i < width * height; i++ )
		{
			switch ( format )
			{
				case 0x01: // ARGB32 stored as BGRA
					outp[i * 4] = data[start + i * 4 + 2]; outp[i * 4 + 1] = data[start + i * 4 + 1];
					outp[i * 4 + 2] = data[start + i * 4]; outp[i * 4 + 3] = data[start + i * 4 + 3];
					break;
				case 0x02:
					outp[i * 4] = data[start + i * 3 + 2]; outp[i * 4 + 1] = data[start + i * 3 + 1];
					outp[i * 4 + 2] = data[start + i * 3]; outp[i * 4 + 3] = 255;
					break;
				case 0x03:
					outp[i * 4] = outp[i * 4 + 1] = outp[i * 4 + 2] = data[start + i * 2]; outp[i * 4 + 3] = data[start + i * 2 + 1];
					break;
				case 0x04:
					outp[i * 4] = outp[i * 4 + 1] = outp[i * 4 + 2] = 255; outp[i * 4 + 3] = data[start + i];
					break;
				default:
					return null;
			}
		}
		img.Mips.Add( outp );
		img.HasAlpha = ImageData.DetectAlpha( outp );
		return img;
	}
}

public static class Dxt
{
	public static byte[] Decode( PixelFormat format, byte[] blocks, int width, int height )
	{
		var outp = new byte[width * height * 4];
		int bw = Math.Max( 1, (width + 3) / 4 ), bh = Math.Max( 1, (height + 3) / 4 );
		int blockSize = format == PixelFormat.Dxt1 ? 8 : 16;
		var colors = new byte[16];
		var alpha = new byte[16];
		var pr = new int[4]; var pg = new int[4]; var pb = new int[4]; var pa = new int[4];

		for ( int by = 0; by < bh; by++ )
			for ( int bx = 0; bx < bw; bx++ )
			{
				int o = (by * bw + bx) * blockSize;
				if ( o + blockSize > blocks.Length ) return outp;

				int colorOffset = o;
				if ( format == PixelFormat.Dxt3 )
				{
					for ( int i = 0; i < 16; i++ )
					{
						int v = (blocks[o + i / 2] >> ((i & 1) * 4)) & 0xf;
						alpha[i] = (byte)(v * 17);
					}
					colorOffset = o + 8;
				}
				else if ( format == PixelFormat.Dxt5 )
				{
					int a0 = blocks[o], a1 = blocks[o + 1];
					ulong bits = 0;
					for ( int i = 0; i < 6; i++ ) bits |= (ulong)blocks[o + 2 + i] << (8 * i);
					for ( int i = 0; i < 16; i++ )
					{
						int code = (int)((bits >> (3 * i)) & 7);
						int a;
						if ( code == 0 ) a = a0;
						else if ( code == 1 ) a = a1;
						else if ( a0 > a1 ) a = ((8 - code) * a0 + (code - 1) * a1) / 7;
						else if ( code == 6 ) a = 0;
						else if ( code == 7 ) a = 255;
						else a = ((6 - code) * a0 + (code - 1) * a1) / 5;
						alpha[i] = (byte)a;
					}
					colorOffset = o + 8;
				}

				int c0 = blocks[colorOffset] | (blocks[colorOffset + 1] << 8);
				int c1 = blocks[colorOffset + 2] | (blocks[colorOffset + 3] << 8);
				Unpack565( c0, out pr[0], out pg[0], out pb[0] );
				Unpack565( c1, out pr[1], out pg[1], out pb[1] );
				pa[0] = pa[1] = pa[2] = pa[3] = 255;
				if ( c0 > c1 || format != PixelFormat.Dxt1 )
				{
					pr[2] = (2 * pr[0] + pr[1]) / 3; pg[2] = (2 * pg[0] + pg[1]) / 3; pb[2] = (2 * pb[0] + pb[1]) / 3;
					pr[3] = (pr[0] + 2 * pr[1]) / 3; pg[3] = (pg[0] + 2 * pg[1]) / 3; pb[3] = (pb[0] + 2 * pb[1]) / 3;
				}
				else
				{
					pr[2] = (pr[0] + pr[1]) / 2; pg[2] = (pg[0] + pg[1]) / 2; pb[2] = (pb[0] + pb[1]) / 2;
					pr[3] = pg[3] = pb[3] = 0; pa[3] = 0;
				}

				uint idx = (uint)(blocks[colorOffset + 4] | (blocks[colorOffset + 5] << 8) | (blocks[colorOffset + 6] << 16) | (blocks[colorOffset + 7] << 24));
				for ( int i = 0; i < 16; i++ )
				{
					int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
					if ( x >= width || y >= height ) continue;
					int c = (int)((idx >> (2 * i)) & 3);
					int d = (y * width + x) * 4;
					outp[d] = (byte)pr[c]; outp[d + 1] = (byte)pg[c]; outp[d + 2] = (byte)pb[c];
					outp[d + 3] = format == PixelFormat.Dxt1 ? (byte)pa[c] : alpha[i];
				}
			}
		return outp;
	}

	static void Unpack565( int c, out int r, out int g, out int b )
	{
		r = ((c >> 11) & 31) * 255 / 31;
		g = ((c >> 5) & 63) * 255 / 63;
		b = (c & 31) * 255 / 31;
	}
}
