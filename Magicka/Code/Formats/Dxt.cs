using System;

namespace Magicka;

/// <summary>DXT1/3/5 decoding, so two character layers can be packed into one texture.</summary>
internal static class Dxt
{
	public static byte[] Decode( Texture2DContent texture )
	{
		var rgba = new byte[texture.Width * texture.Height * 4];
		var data = texture.Levels.Length > 0 ? texture.Levels[0] : null;
		if ( data is null )
			return null;

		switch ( texture.Format )
		{
			case SurfaceFormat.Dxt1:
				DecodeDxt1( data, texture.Width, texture.Height, rgba );
				return rgba;

			case SurfaceFormat.Dxt3:
				DecodeDxt3( data, texture.Width, texture.Height, rgba );
				return rgba;

			case SurfaceFormat.Dxt5:
				DecodeDxt5( data, texture.Width, texture.Height, rgba );
				return rgba;

			case SurfaceFormat.Color:
			case SurfaceFormat.Rgba32:
				for ( var i = 0; i + 3 < data.Length && i + 3 < rgba.Length; i += 4 )
				{
					rgba[i] = data[i + 2];
					rgba[i + 1] = data[i + 1];
					rgba[i + 2] = data[i];
					rgba[i + 3] = data[i + 3];
				}

				return rgba;
		}

		return null;
	}

	static void DecodeDxt1( byte[] data, int width, int height, byte[] rgba )
	{
		for ( var by = 0; by < (height + 3) / 4; by++ )
		for ( var bx = 0; bx < (width + 3) / 4; bx++ )
		{
			var offset = (by * ((width + 3) / 4) + bx) * 8;
			if ( offset + 8 > data.Length )
				return;

			var c0 = (ushort)(data[offset] | (data[offset + 1] << 8));
			var c1 = (ushort)(data[offset + 2] | (data[offset + 3] << 8));
			var bits = BitConverter.ToUInt32( data, offset + 4 );

			Palette( c0, c1, c0 > c1, out var colours );
			var transparent = c0 > c1 ? (byte)0 : (byte)255;

			for ( var py = 0; py < 4; py++ )
			for ( var px = 0; px < 4; px++ )
			{
				var x = bx * 4 + px;
				var y = by * 4 + py;
				if ( x >= width || y >= height )
					continue;

				var index = (int)((bits >> (2 * (py * 4 + px))) & 3);
				var o = (y * width + x) * 4;
				rgba[o] = colours[index * 3];
				rgba[o + 1] = colours[index * 3 + 1];
				rgba[o + 2] = colours[index * 3 + 2];
				rgba[o + 3] = index == 3 ? transparent : (byte)255;
			}
		}
	}

	static void DecodeDxt5( byte[] data, int width, int height, byte[] rgba )
	{
		Span<byte> alphas = stackalloc byte[8];

		for ( var by = 0; by < (height + 3) / 4; by++ )
		for ( var bx = 0; bx < (width + 3) / 4; bx++ )
		{
			var offset = (by * ((width + 3) / 4) + bx) * 16;
			if ( offset + 16 > data.Length )
				return;

			var a0 = data[offset];
			var a1 = data[offset + 1];
			var alphaBits = BitConverter.ToUInt64( data, offset + 2 );
			var c0 = (ushort)(data[offset + 8] | (data[offset + 9] << 8));
			var c1 = (ushort)(data[offset + 10] | (data[offset + 11] << 8));
			var bits = BitConverter.ToUInt32( data, offset + 12 );

			alphas[0] = a0;
			alphas[1] = a1;

			if ( a0 > a1 )
			{
				for ( var i = 1; i < 7; i++ )
					alphas[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
			}
			else
			{
				for ( var i = 1; i < 5; i++ )
					alphas[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);

				alphas[6] = 0;
				alphas[7] = 255;
			}

			Palette( c0, c1, c0 > c1, out var colours );

			for ( var py = 0; py < 4; py++ )
			for ( var px = 0; px < 4; px++ )
			{
				var x = bx * 4 + px;
				var y = by * 4 + py;
				if ( x >= width || y >= height )
					continue;

				var index = (int)((bits >> (2 * (py * 4 + px))) & 3);
				var alphaIndex = (int)((alphaBits >> (3 * (py * 4 + px))) & 7);
				var o = (y * width + x) * 4;
				rgba[o] = colours[index * 3];
				rgba[o + 1] = colours[index * 3 + 1];
				rgba[o + 2] = colours[index * 3 + 2];
				rgba[o + 3] = alphas[alphaIndex];
			}
		}
	}

	static void DecodeDxt3( byte[] data, int width, int height, byte[] rgba )
	{
		for ( var by = 0; by < (height + 3) / 4; by++ )
		for ( var bx = 0; bx < (width + 3) / 4; bx++ )
		{
			var offset = (by * ((width + 3) / 4) + bx) * 16;
			if ( offset + 16 > data.Length )
				return;

			var alphaBits = BitConverter.ToUInt64( data, offset );
			var c0 = (ushort)(data[offset + 8] | (data[offset + 9] << 8));
			var c1 = (ushort)(data[offset + 10] | (data[offset + 11] << 8));
			var bits = BitConverter.ToUInt32( data, offset + 12 );

			Palette( c0, c1, c0 > c1, out var colours );

			for ( var py = 0; py < 4; py++ )
			for ( var px = 0; px < 4; px++ )
			{
				var x = bx * 4 + px;
				var y = by * 4 + py;
				if ( x >= width || y >= height )
					continue;

				var index = (int)((bits >> (2 * (py * 4 + px))) & 3);
				var alpha = (byte)(((alphaBits >> (4 * (py * 4 + px))) & 0xF) * 17);
				var o = (y * width + x) * 4;
				rgba[o] = colours[index * 3];
				rgba[o + 1] = colours[index * 3 + 1];
				rgba[o + 2] = colours[index * 3 + 2];
				rgba[o + 3] = alpha;
			}
		}
	}

	static void Palette( ushort c0, ushort c1, bool fourColours, out byte[] colours )
	{
		colours = new byte[12];

		Rgb( c0, out colours[0], out colours[1], out colours[2] );
		Rgb( c1, out colours[3], out colours[4], out colours[5] );

		if ( fourColours )
		{
			for ( var i = 0; i < 3; i++ )
			{
				colours[6 + i] = (byte)((2 * colours[i] + colours[3 + i]) / 3);
				colours[9 + i] = (byte)((colours[i] + 2 * colours[3 + i]) / 3);
			}
		}
		else
		{
			for ( var i = 0; i < 3; i++ )
			{
				colours[6 + i] = (byte)((colours[i] + colours[3 + i]) / 2);
				colours[9 + i] = 0;
			}
		}
	}

	static void Rgb( ushort colour, out byte r, out byte g, out byte b )
	{
		r = (byte)(((colour >> 11) & 31) * 255 / 31);
		g = (byte)(((colour >> 5) & 63) * 255 / 63);
		b = (byte)((colour & 31) * 255 / 31);
	}
}
