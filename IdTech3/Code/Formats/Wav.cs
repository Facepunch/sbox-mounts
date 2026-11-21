using System;

namespace IdTech3.Formats;

public enum WavEncoding { Unknown, Pcm, ImaAdpcm, MsAdpcm, Mp3, Float }

public sealed class WavInfo
{
	public WavEncoding Encoding;
	public int Channels, SampleRate, BitsPerSample, BlockAlign;
	public int DataOffset, DataLength;
	public int SamplesPerBlock;
	/// <summary>MS ADPCM coefficient table.</summary>
	public short[] Coef1, Coef2;
	/// <summary>Loop start in samples from a "cue " or "smpl" chunk, or -1.</summary>
	public int LoopStart = -1;
}

public static class Wav
{
	public static WavInfo Parse( byte[] data )
	{
		var b = new Bin( data );
		if ( data is null || data.Length < 12 || data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F' ) return null;
		if ( data[8] != 'W' || data[9] != 'A' || data[10] != 'V' || data[11] != 'E' ) return null;

		var info = new WavInfo();
		int o = 12;
		bool haveFmt = false;
		while ( o + 8 <= data.Length )
		{
			string id = System.Text.Encoding.ASCII.GetString( data, o, 4 );
			int size = b.I32( o + 4 );
			int body = o + 8;
			if ( size < 0 ) break;
			int avail = Math.Min( size, data.Length - body );

			if ( id == "fmt " && avail >= 16 )
			{
				int tag = b.U16( body );
				info.Channels = Math.Max( 1, (int)b.U16( body + 2 ) );
				info.SampleRate = b.I32( body + 4 );
				info.BlockAlign = b.U16( body + 12 );
				info.BitsPerSample = b.U16( body + 14 );
				info.Encoding = tag switch
				{
					1 => WavEncoding.Pcm,
					2 => WavEncoding.MsAdpcm,
					3 => WavEncoding.Float,
					0x11 => WavEncoding.ImaAdpcm,
					0x55 => WavEncoding.Mp3,
					0xFFFE => WavEncoding.Pcm,
					_ => WavEncoding.Unknown,
				};
				if ( avail >= 20 ) info.SamplesPerBlock = b.U16( body + 18 );
				if ( info.Encoding == WavEncoding.MsAdpcm && avail >= 22 )
				{
					int n = b.U16( body + 20 );
					info.Coef1 = new short[n]; info.Coef2 = new short[n];
					for ( int i = 0; i < n && body + 22 + i * 4 + 4 <= data.Length; i++ )
					{
						info.Coef1[i] = b.I16( body + 22 + i * 4 );
						info.Coef2[i] = b.I16( body + 24 + i * 4 );
					}
				}
				haveFmt = true;
			}
			else if ( id == "data" )
			{
				info.DataOffset = body;
				info.DataLength = avail;
			}
			else if ( id == "cue " && avail >= 28 && b.I32( body ) > 0 )
			{
				info.LoopStart = b.I32( body + 4 + 20 );
			}

			o = body + size + (size & 1);
		}
		return haveFmt && info.DataLength > 0 ? info : null;
	}

	/// <summary>Decode ADPCM/float data to signed 16-bit interleaved PCM bytes.</summary>
	public static byte[] DecodeToPcm16( byte[] data, WavInfo info )
	{
		return info.Encoding switch
		{
			WavEncoding.ImaAdpcm => DecodeIma( data, info ),
			WavEncoding.MsAdpcm => DecodeMs( data, info ),
			WavEncoding.Float => DecodeFloat( data, info ),
			_ => null,
		};
	}

	static readonly int[] ImaIndex = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
	static readonly int[] ImaStep =
	{
		7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
		157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552,
		1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487,
		12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
	};

	static byte[] DecodeIma( byte[] data, WavInfo info )
	{
		int ch = info.Channels;
		int block = info.BlockAlign > 0 ? info.BlockAlign : 512 * ch;
		int spb = info.SamplesPerBlock > 0 ? info.SamplesPerBlock : (block - 4 * ch) * 8 / (4 * ch) + 1;
		int blocks = (info.DataLength + block - 1) / block;
		var outp = new short[blocks * spb * ch];
		int written = 0;
		var pred = new int[ch]; var index = new int[ch];

		for ( int bi = 0; bi < blocks; bi++ )
		{
			int bo = info.DataOffset + bi * block;
			int bend = Math.Min( info.DataOffset + info.DataLength, bo + block );
			if ( bo + 4 * ch > bend ) break;

			int frameStart = written;
			for ( int c = 0; c < ch; c++ )
			{
				pred[c] = (short)(data[bo + c * 4] | (data[bo + c * 4 + 1] << 8));
				index[c] = Math.Clamp( (int)data[bo + c * 4 + 2], 0, 88 );
				outp[frameStart + c] = (short)pred[c];
			}
			int samplesThisBlock = 1;
			int p = bo + 4 * ch;

			// 4 bytes (8 samples) per channel, interleaved
			while ( p + 4 * ch <= bend && samplesThisBlock + 8 <= spb )
			{
				for ( int c = 0; c < ch; c++ )
				{
					for ( int k = 0; k < 8; k++ )
					{
						int nib = (data[p + c * 4 + k / 2] >> ((k & 1) * 4)) & 0xf;
						int step = ImaStep[index[c]];
						int diff = step >> 3;
						if ( (nib & 1) != 0 ) diff += step >> 2;
						if ( (nib & 2) != 0 ) diff += step >> 1;
						if ( (nib & 4) != 0 ) diff += step;
						if ( (nib & 8) != 0 ) diff = -diff;
						pred[c] = Math.Clamp( pred[c] + diff, -32768, 32767 );
						index[c] = Math.Clamp( index[c] + ImaIndex[nib], 0, 88 );
						int si = frameStart + (samplesThisBlock + k) * ch + c;
						if ( si < outp.Length ) outp[si] = (short)pred[c];
					}
				}
				samplesThisBlock += 8;
				p += 4 * ch;
			}
			written = frameStart + samplesThisBlock * ch;
		}
		return ToBytes( outp, written );
	}

	static readonly int[] MsAdapt = { 230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230 };

	static byte[] DecodeMs( byte[] data, WavInfo info )
	{
		int ch = info.Channels;
		int block = info.BlockAlign > 0 ? info.BlockAlign : 256 * ch;
		int spb = info.SamplesPerBlock > 0 ? info.SamplesPerBlock : (block - 7 * ch) * 2 / ch + 2;
		var c1 = info.Coef1 ?? new short[] { 256, 512, 0, 192, 240, 460, 392 };
		var c2 = info.Coef2 ?? new short[] { 0, -256, 0, 64, 0, -208, -232 };
		int blocks = (info.DataLength + block - 1) / block;
		var outp = new short[blocks * spb * ch];
		int written = 0;

		var pred = new int[ch]; var delta = new int[ch]; var s1 = new int[ch]; var s2 = new int[ch];
		for ( int bi = 0; bi < blocks; bi++ )
		{
			int bo = info.DataOffset + bi * block;
			int bend = Math.Min( info.DataOffset + info.DataLength, bo + block );
			if ( bo + 7 * ch > bend ) break;
			var b = new Bin( data );
			int p = bo;
			for ( int c = 0; c < ch; c++ ) pred[c] = Math.Clamp( (int)data[p++], 0, c1.Length - 1 );
			for ( int c = 0; c < ch; c++ ) { delta[c] = b.I16( p ); p += 2; }
			for ( int c = 0; c < ch; c++ ) { s1[c] = b.I16( p ); p += 2; }
			for ( int c = 0; c < ch; c++ ) { s2[c] = b.I16( p ); p += 2; }

			int start = written;
			for ( int c = 0; c < ch; c++ ) outp[start + c] = (short)s2[c];
			for ( int c = 0; c < ch; c++ ) outp[start + ch + c] = (short)s1[c];
			int n = 2 * ch;

			int c0 = 0;
			while ( p < bend && n < spb * ch )
			{
				int byt = data[p++];
				for ( int half = 0; half < 2 && n < spb * ch; half++ )
				{
					int nib = half == 0 ? byt >> 4 : byt & 0xf;
					int c = c0;
					int signed = nib >= 8 ? nib - 16 : nib;
					int predicted = (s1[c] * c1[pred[c]] + s2[c] * c2[pred[c]]) >> 8;
					int sample = Math.Clamp( predicted + signed * delta[c], -32768, 32767 );
					s2[c] = s1[c]; s1[c] = sample;
					delta[c] = Math.Max( 16, (MsAdapt[nib] * delta[c]) >> 8 );
					outp[start + n] = (short)sample;
					n++;
					c0 = (c0 + 1) % ch;
				}
			}
			written = start + n;
		}
		return ToBytes( outp, written );
	}

	static byte[] DecodeFloat( byte[] data, WavInfo info )
	{
		if ( info.BitsPerSample != 32 ) return null;
		int count = info.DataLength / 4;
		var outp = new short[count];
		var b = new Bin( data );
		for ( int i = 0; i < count; i++ )
			outp[i] = (short)Math.Clamp( (int)(b.F32( info.DataOffset + i * 4 ) * 32767f), -32768, 32767 );
		return ToBytes( outp, count );
	}

	static byte[] ToBytes( short[] s, int count )
	{
		count = Math.Min( count, s.Length );
		var r = new byte[count * 2];
		for ( int i = 0; i < count; i++ ) { r[i * 2] = (byte)s[i]; r[i * 2 + 1] = (byte)(s[i] >> 8); }
		return r;
	}
}
