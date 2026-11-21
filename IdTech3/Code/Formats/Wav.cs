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
}
