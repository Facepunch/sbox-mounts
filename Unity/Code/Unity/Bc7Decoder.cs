using System.Buffers.Binary;

namespace UnityMount;

/// <summary>
/// Pure-C# Khronos BC7 (BPTC) block decoder supporting all 8 modes (0–7),
/// producing RGBA8888 pixels with optional vertical flip for Unity textures.
/// </summary>
public static class Bc7Decoder
{
	static readonly byte[] PartitionTable2 =
	[
		0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1, 0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1,
		0,1,1,1,0,1,1,1,0,1,1,1,0,1,1,1, 0,0,0,1,0,0,1,1,0,0,1,1,0,1,1,1,
		0,0,0,0,0,0,0,1,0,0,0,1,0,0,1,1, 0,0,1,1,0,1,1,1,0,1,1,1,1,1,1,1,
		0,0,0,1,0,0,1,1,0,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,1,0,0,1,1,0,1,1,1,
		0,0,0,0,0,0,0,0,0,0,0,1,0,0,1,1, 0,0,1,1,0,1,1,1,1,1,1,1,1,1,1,1,
		0,0,0,0,0,0,0,1,0,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,0,0,0,0,1,0,1,1,1,
		0,0,0,1,0,1,1,1,1,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1,
		0,0,0,0,1,1,1,1,1,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,0,0,0,0,0,1,1,1,1,
		0,0,0,0,1,0,0,0,1,1,1,0,1,1,1,1, 0,1,1,1,0,0,0,1,0,0,0,0,0,0,0,0,
		0,0,0,0,0,0,0,0,1,0,0,0,1,1,1,0, 0,1,1,1,0,0,1,1,0,0,0,1,0,0,0,0,
		0,0,1,1,0,0,0,1,0,0,0,0,0,0,0,0, 0,0,0,0,1,0,0,0,1,1,0,0,1,1,1,0,
		0,0,0,0,0,0,0,0,1,0,0,0,1,1,0,0, 0,1,1,1,0,0,1,1,0,0,1,1,0,0,0,1,
		0,0,1,1,0,0,0,1,0,0,0,1,0,0,0,0, 0,0,0,0,1,0,0,0,1,0,0,0,1,1,0,0,
		0,1,1,0,0,1,1,0,0,1,1,0,0,1,1,0, 0,0,1,1,0,1,1,0,0,1,1,0,1,1,0,0,
		0,0,0,1,0,1,1,1,1,1,1,0,1,0,0,0, 0,0,0,0,1,1,1,1,1,1,1,1,0,0,0,0,
		0,1,1,1,0,0,0,1,1,0,0,0,1,1,1,0, 0,0,1,1,1,0,0,1,1,0,0,1,1,1,0,0,
		0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1, 0,0,0,0,1,1,1,1,0,0,0,0,1,1,1,1,
		0,1,0,1,1,0,1,0,0,1,0,1,1,0,1,0, 0,0,1,1,0,0,1,1,1,1,0,0,1,1,0,0,
		0,0,1,1,1,1,0,0,0,0,1,1,1,1,0,0, 0,1,0,1,0,1,0,1,1,0,1,0,1,0,1,0,
		0,1,1,0,1,0,0,1,0,1,1,0,1,0,0,1, 0,1,0,1,1,0,1,0,1,0,1,0,0,1,0,1,
		0,1,1,1,0,0,1,1,1,1,0,0,1,1,1,0, 0,0,0,1,0,0,1,1,1,1,0,0,1,0,0,0,
		0,0,1,1,0,0,1,0,0,1,0,0,1,1,0,0, 0,0,1,1,1,0,1,1,1,1,0,1,1,1,0,0,
		0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0, 0,0,1,1,1,1,0,0,1,1,0,0,0,0,1,1,
		0,1,1,0,0,1,1,0,1,0,0,1,1,0,0,1, 0,0,0,0,0,1,1,0,0,1,1,0,0,0,0,0,
		0,1,0,0,1,1,1,0,0,1,0,0,0,0,0,0, 0,0,1,0,0,1,1,1,0,0,1,0,0,0,0,0,
		0,0,0,0,0,0,1,0,0,1,1,1,0,0,1,0, 0,0,0,0,0,1,0,0,1,1,1,0,0,1,0,0,
		0,1,1,0,1,1,0,0,1,0,0,1,0,0,1,1, 0,0,1,1,0,1,1,0,1,1,0,0,1,0,0,1,
		0,1,1,0,0,0,1,1,1,0,0,1,1,1,0,0, 0,0,1,1,1,0,0,1,1,1,0,0,0,1,1,0,
		0,1,1,0,1,1,0,0,1,1,0,0,1,0,0,1, 0,1,1,0,0,0,1,1,0,0,1,1,1,0,0,1,
		0,1,1,1,1,1,1,0,1,0,0,0,0,0,0,1, 0,0,0,1,1,0,0,0,1,1,1,0,0,1,1,1,
		0,0,0,0,1,1,1,1,0,0,1,1,0,0,1,1, 0,0,1,1,0,0,1,1,1,1,1,1,0,0,0,0,
		0,0,1,0,0,0,1,0,1,1,1,0,1,1,1,0, 0,1,0,0,0,1,0,0,0,1,1,1,0,1,1,1
	];

	static readonly byte[] PartitionTable3 =
	[
		0,0,1,1,0,0,1,1,0,2,2,1,2,2,2,2, 0,0,0,1,0,0,1,1,2,2,1,1,2,2,2,1,
		0,0,0,0,2,0,0,1,2,2,1,1,2,2,1,1, 0,2,2,2,0,0,2,2,0,0,1,1,0,1,1,1,
		0,0,0,0,0,0,0,0,1,1,2,2,1,1,2,2, 0,0,1,1,0,0,1,1,0,0,2,2,0,0,2,2,
		0,0,2,2,0,0,2,2,1,1,1,1,1,1,1,1, 0,0,1,1,0,0,1,1,2,2,1,1,2,2,1,1,
		0,0,0,0,0,0,0,0,1,1,1,1,2,2,2,2, 0,0,0,0,1,1,1,1,1,1,1,1,2,2,2,2,
		0,0,0,0,1,1,1,1,2,2,2,2,2,2,2,2, 0,0,1,2,0,0,1,2,0,0,1,2,0,0,1,2,
		0,1,1,2,0,1,1,2,0,1,1,2,0,1,1,2, 0,1,2,2,0,1,2,2,0,1,2,2,0,1,2,2,
		0,0,1,1,0,1,1,2,1,1,2,2,1,2,2,2, 0,0,1,1,2,0,0,1,2,2,0,0,2,2,2,0,
		0,0,0,1,0,0,1,1,0,1,1,2,1,1,2,2, 0,1,1,1,0,0,1,1,2,0,0,1,2,2,0,0,
		0,0,0,0,1,1,2,2,1,1,2,2,1,1,2,2, 0,0,2,2,0,0,2,2,0,0,2,2,1,1,1,1,
		0,1,1,1,0,1,1,1,0,2,2,2,0,2,2,2, 0,0,0,1,0,0,0,1,2,2,2,1,2,2,2,1,
		0,0,0,0,0,0,1,1,0,1,2,2,0,1,2,2, 0,0,0,0,1,1,0,0,2,2,1,0,2,2,1,0,
		0,1,2,2,0,1,2,2,0,0,1,1,0,0,0,0, 0,0,1,2,0,0,1,2,1,1,2,2,2,2,2,2,
		0,1,1,0,1,2,2,1,1,2,2,1,0,1,1,0, 0,0,0,0,0,1,1,0,1,2,2,1,1,2,2,1,
		0,0,2,2,1,1,0,2,1,1,0,2,0,0,2,2, 0,1,1,0,0,1,1,0,2,0,0,2,2,2,2,2,
		0,0,1,1,0,1,2,2,0,1,2,2,0,0,1,1, 0,0,0,0,2,0,0,0,2,2,1,1,2,2,2,1,
		0,0,0,0,0,0,0,2,1,1,2,2,1,2,2,2, 0,2,2,2,0,0,2,2,0,0,1,2,0,0,1,1,
		0,0,1,1,0,0,1,2,0,0,2,2,0,2,2,2, 0,1,2,0,0,1,2,0,0,1,2,0,0,1,2,0,
		0,0,0,0,1,1,1,1,2,2,2,2,0,0,0,0, 0,1,2,0,1,2,0,1,2,0,1,2,0,1,2,0,
		0,1,2,0,2,0,1,2,1,2,0,1,0,1,2,0, 0,0,1,1,2,2,0,0,1,1,2,2,0,0,1,1,
		0,0,1,1,1,1,2,2,2,2,0,0,0,0,1,1, 0,1,0,1,0,1,0,1,2,2,2,2,2,2,2,2,
		0,0,0,0,0,0,0,0,2,1,2,1,2,1,2,1, 0,0,2,2,1,1,2,2,0,0,2,2,1,1,2,2,
		0,0,2,2,0,0,1,1,0,0,2,2,0,0,1,1, 0,2,2,0,1,2,2,1,0,2,2,0,1,2,2,1,
		0,1,0,1,2,2,2,2,2,2,2,2,0,1,0,1, 0,0,0,0,2,1,2,1,2,1,2,1,2,1,2,1,
		0,1,0,1,0,1,0,1,0,1,0,1,2,2,2,2, 0,2,2,2,0,1,1,1,0,2,2,2,0,1,1,1,
		0,0,0,2,1,1,1,2,0,0,0,2,1,1,1,2, 0,0,0,0,2,1,1,2,2,1,1,2,2,1,1,2,
		0,2,2,2,0,1,1,1,0,1,1,1,0,2,2,2, 0,0,0,2,1,1,1,2,1,1,1,2,0,0,0,2,
		0,1,1,0,0,1,1,0,0,1,1,0,2,2,2,2, 0,0,0,0,0,0,0,0,2,1,1,2,2,1,1,2,
		0,1,1,0,0,1,1,0,2,2,2,2,2,2,2,2, 0,0,2,2,0,0,1,1,0,0,1,1,0,0,2,2,
		0,0,2,2,1,1,2,2,1,1,2,2,0,0,2,2, 0,0,0,0,0,0,0,0,0,0,0,0,2,1,1,2,
		0,0,0,2,0,0,0,1,0,0,0,2,0,0,0,1, 0,2,2,2,1,2,2,2,0,2,2,2,1,2,2,2,
		0,1,0,1,2,2,2,2,2,2,2,2,2,2,2,2, 0,1,1,1,2,0,1,1,2,2,0,1,2,2,2,0
	];

	static readonly byte[] AnchorIndex2 =
	[
		15,15,15,15,15,15,15,15,15,15,15,15,15,15,15,15,
		15, 2, 8, 2, 2, 8, 8,15, 2, 8, 2, 2, 8, 8, 2, 2,
		15,15, 6, 8, 2, 8,15,15, 2, 8, 2, 2, 2,15,15, 6,
		 6, 2, 6, 8,15,15, 2, 2,15,15,15,15,15, 2, 2,15
	];

	static readonly byte[] AnchorIndex3A =
	[
		 3, 3,15,15, 8, 3,15,15, 8, 8, 6, 6, 6, 5, 3, 3,
		 3, 3, 8,15, 3, 3, 6,10, 5, 8, 8, 6, 8, 5,15,15,
		 8,15, 3, 5, 6,10, 8,15,15, 3,15, 5,15,15,15,15,
		 3,15, 5, 5, 5, 8, 5,10, 5,10, 8,13,15,12, 3, 3
	];

	static readonly byte[] AnchorIndex3B =
	[
		15, 8, 8, 3,15,15, 3, 8,15,15,15,15,15,15,15, 8,
		15, 8,15, 3,15, 8,15, 8, 3,15, 6,10,15,15,10, 8,
		15, 3,15,10,10, 8, 9,10, 6,15, 8,15, 3, 6, 6, 8,
		15, 3,15,15,15,15,15,15,15,15,15,15, 3,15,15, 8
	];

	static readonly byte[] Weights2 = [0, 21, 43, 64];
	static readonly byte[] Weights3 = [0, 9, 18, 27, 37, 46, 55, 64];
	static readonly byte[] Weights4 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

	// Mode parameters: NS, PB, RB, ISB, CB, AB, EPB, SPB, IB, IB2
	static readonly byte[] ModeNS = [3, 2, 3, 2, 1, 1, 1, 2];
	static readonly byte[] ModePB = [4, 6, 6, 6, 0, 0, 0, 6];
	static readonly byte[] ModeRB = [0, 0, 0, 0, 2, 2, 0, 0];
	static readonly byte[] ModeISB = [0, 0, 0, 0, 1, 0, 0, 0];
	static readonly byte[] ModeCB = [4, 6, 5, 7, 5, 7, 7, 5];
	static readonly byte[] ModeAB = [0, 0, 0, 0, 6, 8, 7, 5];
	static readonly byte[] ModeEPB = [1, 0, 0, 1, 0, 0, 1, 0];
	static readonly byte[] ModeSPB = [0, 1, 0, 0, 0, 0, 0, 0];
	static readonly byte[] ModeIB = [3, 3, 2, 2, 2, 2, 4, 2];
	static readonly byte[] ModeIB2 = [0, 0, 0, 0, 3, 2, 0, 0];

	public static byte[] Decode( ReadOnlySpan<byte> src, int width, int height, bool flipY )
	{
		var dst = new byte[width * height * 4];
		int bx = (width + 3) >> 2;
		int by = (height + 3) >> 2;
		Span<byte> blockRgba = stackalloc byte[64];

		for ( int ty = 0; ty < by; ty++ )
		{
			for ( int tx = 0; tx < bx; tx++ )
			{
				int blockOffset = (ty * bx + tx) * 16;
				if ( blockOffset + 16 > src.Length ) continue;

				DecodeBlock( src.Slice( blockOffset, 16 ), blockRgba );

				for ( int py = 0; py < 4; py++ )
				{
					int y = ty * 4 + py;
					if ( y >= height ) continue;
					int outY = flipY ? (height - 1 - y) : y;

					for ( int px = 0; px < 4; px++ )
					{
						int x = tx * 4 + px;
						if ( x >= width ) continue;
						int si = (py * 4 + px) * 4;
						int di = (outY * width + x) * 4;
						dst[di + 0] = blockRgba[si + 0];
						dst[di + 1] = blockRgba[si + 1];
						dst[di + 2] = blockRgba[si + 2];
						dst[di + 3] = blockRgba[si + 3];
					}
				}
			}
		}

		return dst;
	}

	static void DecodeBlock( ReadOnlySpan<byte> block, Span<byte> output )
	{
		ulong lo = BinaryPrimitives.ReadUInt64LittleEndian( block[..8] );
		ulong hi = BinaryPrimitives.ReadUInt64LittleEndian( block[8..16] );
		int bitPos = 0;

		int mode = 0;
		while ( mode < 8 && ReadBits( lo, hi, ref bitPos, 1 ) == 0 )
			mode++;

		if ( mode >= 8 )
		{
			output.Clear();
			return;
		}

		int ns = ModeNS[mode];
		int pb = ModePB[mode];
		int rb = ModeRB[mode];
		int isb = ModeISB[mode];
		int cb = ModeCB[mode];
		int ab = ModeAB[mode];
		int epb = ModeEPB[mode];
		int spb = ModeSPB[mode];
		int ib = ModeIB[mode];
		int ib2 = ModeIB2[mode];

		int partition = pb > 0 ? ReadBits( lo, hi, ref bitPos, pb ) : 0;
		int rotation = rb > 0 ? ReadBits( lo, hi, ref bitPos, rb ) : 0;
		int idxMode = isb > 0 ? ReadBits( lo, hi, ref bitPos, isb ) : 0;

		int numEndpoints = ns * 2;
		Span<int> r = stackalloc int[6];
		Span<int> g = stackalloc int[6];
		Span<int> b = stackalloc int[6];
		Span<int> a = stackalloc int[6];

		for ( int i = 0; i < numEndpoints; i++ ) r[i] = ReadBits( lo, hi, ref bitPos, cb );
		for ( int i = 0; i < numEndpoints; i++ ) g[i] = ReadBits( lo, hi, ref bitPos, cb );
		for ( int i = 0; i < numEndpoints; i++ ) b[i] = ReadBits( lo, hi, ref bitPos, cb );
		for ( int i = 0; i < numEndpoints; i++ ) a[i] = ab > 0 ? ReadBits( lo, hi, ref bitPos, ab ) : 255;

		int colorPrec = cb;
		int alphaPrec = ab;

		if ( epb > 0 )
		{
			for ( int i = 0; i < numEndpoints; i++ )
			{
				int p = ReadBits( lo, hi, ref bitPos, 1 );
				r[i] = (r[i] << 1) | p;
				g[i] = (g[i] << 1) | p;
				b[i] = (b[i] << 1) | p;
				if ( ab > 0 ) a[i] = (a[i] << 1) | p;
			}
			colorPrec++;
			if ( ab > 0 ) alphaPrec++;
		}
		else if ( spb > 0 )
		{
			for ( int s = 0; s < ns; s++ )
			{
				int p = ReadBits( lo, hi, ref bitPos, 1 );
				for ( int k = 0; k < 2; k++ )
				{
					int idx = s * 2 + k;
					r[idx] = (r[idx] << 1) | p;
					g[idx] = (g[idx] << 1) | p;
					b[idx] = (b[idx] << 1) | p;
				}
			}
			colorPrec++;
		}

		for ( int i = 0; i < numEndpoints; i++ )
		{
			r[i] = Unquantize( r[i], colorPrec );
			g[i] = Unquantize( g[i], colorPrec );
			b[i] = Unquantize( b[i], colorPrec );
			if ( ab > 0 ) a[i] = Unquantize( a[i], alphaPrec );
		}

		Span<int> idx1 = stackalloc int[16];
		Span<int> idx2 = stackalloc int[16];

		int anchor1 = ns == 2 ? AnchorIndex2[partition] : (ns == 3 ? AnchorIndex3A[partition] : -1);
		int anchor2 = ns == 3 ? AnchorIndex3B[partition] : -1;

		for ( int i = 0; i < 16; i++ )
		{
			bool isAnchor = i == 0 || i == anchor1 || i == anchor2;
			idx1[i] = ReadBits( lo, hi, ref bitPos, isAnchor ? ib - 1 : ib );
		}

		if ( ib2 > 0 )
		{
			for ( int i = 0; i < 16; i++ )
				idx2[i] = ReadBits( lo, hi, ref bitPos, i == 0 ? ib2 - 1 : ib2 );
		}

		for ( int i = 0; i < 16; i++ )
		{
			int subset = ns switch
			{
				2 => PartitionTable2[partition * 16 + i],
				3 => PartitionTable3[partition * 16 + i],
				_ => 0
			};

			int e0 = subset * 2;
			int e1 = e0 + 1;

			int cw, aw;
			if ( ib2 == 0 )
			{
				cw = GetWeight( ib, idx1[i] );
				aw = cw;
			}
			else if ( idxMode == 0 )
			{
				cw = GetWeight( ib, idx1[i] );
				aw = GetWeight( ib2, idx2[i] );
			}
			else
			{
				cw = GetWeight( ib2, idx2[i] );
				aw = GetWeight( ib, idx1[i] );
			}

			byte pr = (byte)(((64 - cw) * r[e0] + cw * r[e1] + 32) >> 6);
			byte pg = (byte)(((64 - cw) * g[e0] + cw * g[e1] + 32) >> 6);
			byte pbv = (byte)(((64 - cw) * b[e0] + cw * b[e1] + 32) >> 6);
			byte pa = ab > 0 ? (byte)(((64 - aw) * a[e0] + aw * a[e1] + 32) >> 6) : (byte)255;

			switch ( rotation )
			{
				case 1: (pa, pr) = (pr, pa); break;
				case 2: (pa, pg) = (pg, pa); break;
				case 3: (pa, pbv) = (pbv, pa); break;
			}

			int oi = i * 4;
			output[oi + 0] = pr;
			output[oi + 1] = pg;
			output[oi + 2] = pbv;
			output[oi + 3] = pa;
		}
	}

	static int ReadBits( ulong lo, ulong hi, ref int bitPos, int count )
	{
		if ( count <= 0 ) return 0;
		uint val;
		if ( bitPos + count <= 64 )
		{
			val = (uint)((lo >> bitPos) & ((1UL << count) - 1));
		}
		else if ( bitPos >= 64 )
		{
			val = (uint)((hi >> (bitPos - 64)) & ((1UL << count) - 1));
		}
		else
		{
			int loBits = 64 - bitPos;
			ulong lowPart = lo >> bitPos;
			ulong highPart = hi & ((1UL << (count - loBits)) - 1);
			val = (uint)(lowPart | (highPart << loBits));
		}
		bitPos += count;
		return (int)val;
	}

	static int Unquantize( int v, int bits )
		=> (v << (8 - bits)) | (v >> (2 * bits - 8));

	static int GetWeight( int indexBits, int index ) => indexBits switch
	{
		2 => Weights2[index],
		3 => Weights3[index],
		4 => Weights4[index],
		_ => 0
	};
}
