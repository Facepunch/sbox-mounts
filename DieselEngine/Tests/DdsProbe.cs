using System.Text;

/// <summary>Minimal mip-0 RGBA decoder (DXT1/3/5, 32-bit BGRA/RGBA, 8-bit L/A) for research dumps.</summary>
static class DdsProbe
{
	public sealed record Image( int Width, int Height, string Format, float[] Rgba )
	{
		public (float R, float G, float B, float A) At( int x, int y )
		{
			int i = (Math.Clamp( y, 0, Height - 1 ) * Width + Math.Clamp( x, 0, Width - 1 )) * 4;
			return (Rgba[i], Rgba[i + 1], Rgba[i + 2], Rgba[i + 3]);
		}
	}

	public static Image Decode( byte[] dds )
	{
		if ( dds is null || dds.Length < 128 || BitConverter.ToUInt32( dds, 0 ) != 0x20534444 ) return null;
		int height = BitConverter.ToInt32( dds, 12 ), width = BitConverter.ToInt32( dds, 16 );
		uint flags = BitConverter.ToUInt32( dds, 80 );
		string fourCc = Encoding.ASCII.GetString( dds, 84, 4 );
		int bits = BitConverter.ToInt32( dds, 88 );
		uint rMask = BitConverter.ToUInt32( dds, 92 ), gMask = BitConverter.ToUInt32( dds, 96 ), bMask = BitConverter.ToUInt32( dds, 100 ), aMask = BitConverter.ToUInt32( dds, 104 );
		var rgba = new float[width * height * 4];
		if ( (flags & 4) != 0 && fourCc is "DXT1" or "DXT3" or "DXT5" )
		{
			int blockBytes = fourCc == "DXT1" ? 8 : 16;
			int bw = (width + 3) / 4, bh = (height + 3) / 4;
			if ( 128 + (long)bw * bh * blockBytes > dds.Length ) return null;
			for ( int by = 0; by < bh; by++ )
				for ( int bx = 0; bx < bw; bx++ )
				{
					int p = 128 + (by * bw + bx) * blockBytes;
					var alpha = new float[16];
					Array.Fill( alpha, 1f );
					if ( fourCc == "DXT3" )
						for ( int t = 0; t < 16; t++ ) alpha[t] = ((dds[p + t / 2] >> (t % 2 * 4)) & 15) / 15f;
					else if ( fourCc == "DXT5" )
					{
						float a0 = dds[p] / 255f, a1 = dds[p + 1] / 255f;
						ulong indices = 0;
						for ( int k = 0; k < 6; k++ ) indices |= (ulong)dds[p + 2 + k] << (8 * k);
						for ( int t = 0; t < 16; t++ )
						{
							int c = (int)((indices >> (3 * t)) & 7);
							alpha[t] = c == 0 ? a0 : c == 1 ? a1 : a0 > a1 ? ((8 - c) * a0 + (c - 1) * a1) / 7 : c == 6 ? 0 : c == 7 ? 1 : ((6 - c) * a0 + (c - 1) * a1) / 5;
						}
					}
					int q = p + blockBytes - 8;
					ushort c0 = BitConverter.ToUInt16( dds, q ), c1 = BitConverter.ToUInt16( dds, q + 2 );
					uint codes = BitConverter.ToUInt32( dds, q + 4 );
					var pal = new (float R, float G, float B)[4];
					pal[0] = Rgb565( c0 ); pal[1] = Rgb565( c1 );
					if ( c0 > c1 || blockBytes == 16 )
					{
						pal[2] = Mix( pal[0], pal[1], 1 / 3f ); pal[3] = Mix( pal[0], pal[1], 2 / 3f );
					}
					else { pal[2] = Mix( pal[0], pal[1], 0.5f ); pal[3] = (0, 0, 0); }
					for ( int t = 0; t < 16; t++ )
					{
						int x = bx * 4 + t % 4, y = by * 4 + t / 4;
						if ( x >= width || y >= height ) continue;
						var c = pal[(codes >> (t * 2)) & 3];
						int i = (y * width + x) * 4;
						rgba[i] = c.R; rgba[i + 1] = c.G; rgba[i + 2] = c.B; rgba[i + 3] = alpha[t];
					}
				}
			return new( width, height, fourCc, rgba );
		}
		if ( (flags & 0x40) != 0 || (flags & 0x20000) != 0 || (flags & 2) != 0 )
		{
			int stride = bits / 8;
			if ( stride < 1 || 128 + (long)width * height * stride > dds.Length ) return null;
			for ( int i = 0; i < width * height; i++ )
			{
				uint v = 0;
				for ( int k = 0; k < stride; k++ ) v |= (uint)dds[128 + i * stride + k] << (8 * k);
				float Channel( uint mask ) => mask == 0 ? 0 : (v & mask) / (float)mask;
				float r = Channel( rMask ), g = gMask == 0 ? r : Channel( gMask ), b = bMask == 0 ? r : Channel( bMask );
				rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = aMask == 0 ? 1 : Channel( aMask );
			}
			return new( width, height, $"{bits}bpp masks {rMask:x}/{gMask:x}/{bMask:x}/{aMask:x}", rgba );
		}
		return null;
	}

	/// <summary>Print a down-sampled grid of RGBA values, top row first.</summary>
	public static void Grid( TextWriter o, Image image, int columns = 8, int rows = 8 )
	{
		o.WriteLine( $"    {image.Width}x{image.Height} {image.Format}; rows top→bottom, columns left→right, values r,g,b,a" );
		for ( int r = 0; r < rows; r++ )
		{
			int y = (int)((r + 0.5f) / rows * image.Height);
			var cells = new List<string>();
			for ( int c = 0; c < columns; c++ )
			{
				var (R, G, B, A) = image.At( (int)((c + 0.5f) / columns * image.Width ), y );
				cells.Add( $"{R:0.00},{G:0.00},{B:0.00},{A:0.00}" );
			}
			o.WriteLine( $"    y={y,5}: {string.Join( "  ", cells )}" );
		}
	}

	static (float R, float G, float B) Rgb565( ushort c ) => (((c >> 11) & 31) / 31f, ((c >> 5) & 63) / 63f, (c & 31) / 31f);
	static (float R, float G, float B) Mix( (float R, float G, float B) a, (float R, float G, float B) b, float t ) =>
		(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
}
