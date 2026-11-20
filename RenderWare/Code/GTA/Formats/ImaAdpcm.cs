using System;

namespace Renderware;

/// <summary>
/// Decodes IMA (DVI) ADPCM .wav files - format tag 0x11, what GTA III's audio/*.wav ambient streams use - to 16-bit PCM.
/// </summary>
// todo: move to engine?
public static class ImaAdpcmWav
{
	static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

	static readonly int[] StepTable =
	{
		7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
		50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307,
		337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
		2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
		15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
	};

	public readonly record struct Pcm( byte[] Data, int Channels, uint Rate );

	/// <summary>True if the data is a RIFF wave in IMA ADPCM.</summary>
	public static bool IsImaAdpcm( byte[] file ) => file.Length > 22 && file[0] == 'R' && file[1] == 'I' && file[2] == 'F' && file[3] == 'F' && file[20] == 0x11 && file[21] == 0;

	public static Pcm Decode( byte[] file )
	{
		using var br = new BinaryReader( new MemoryStream( file, false ) );

		if ( new string( br.ReadChars( 4 ) ) != "RIFF" )
			throw new InvalidDataException( "Not a RIFF file" );
		br.ReadUInt32();
		if ( new string( br.ReadChars( 4 ) ) != "WAVE" )
			throw new InvalidDataException( "Not a WAVE file" );

		int channels = 0, blockAlign = 0, samplesPerBlock = 0;
		uint rate = 0;
		byte[] data = null;

		while ( br.BaseStream.Position + 8 <= br.BaseStream.Length )
		{
			var id = new string( br.ReadChars( 4 ) );
			int size = br.ReadInt32();
			long next = br.BaseStream.Position + size + (size & 1);

			if ( id == "fmt " )
			{
				int tag = br.ReadUInt16();
				if ( tag != 0x11 )
					throw new InvalidDataException( $"Expected IMA ADPCM (0x11), got format 0x{tag:x}" );

				channels = br.ReadUInt16();
				rate = br.ReadUInt32();
				br.ReadUInt32(); // byte rate
				blockAlign = br.ReadUInt16();
				br.ReadUInt16(); // bits per sample (4)

				// extra: cbSize, samples per block - derive it if it's missing
				samplesPerBlock = size >= 20 && br.ReadUInt16() >= 2 ? br.ReadUInt16() : 0;
			}
			else if ( id == "data" )
			{
				data = br.ReadBytes( (int)Math.Min( size, br.BaseStream.Length - br.BaseStream.Position ) );
			}

			br.BaseStream.Position = Math.Min( next, br.BaseStream.Length );
		}

		if ( data is null || channels < 1 || blockAlign <= 4 * channels )
			throw new InvalidDataException( "Missing or invalid fmt/data chunk" );

		// each block: a 4 byte header per channel (first sample + step index), then 4 byte groups of 8 nibbles per channel
		if ( samplesPerBlock <= 0 )
			samplesPerBlock = (blockAlign - 4 * channels) * 8 / (4 * channels) + 1;

		int blocks = data.Length / blockAlign;
		var pcm = new short[blocks * samplesPerBlock * channels];
		var predictor = new int[channels];
		var index = new int[channels];

		for ( int b = 0; b < blocks; b++ )
		{
			int o = b * blockAlign;
			int baseSample = b * samplesPerBlock;

			for ( int c = 0; c < channels; c++ )
			{
				predictor[c] = (short)(data[o] | (data[o + 1] << 8));
				index[c] = Math.Clamp( (int)data[o + 2], 0, 88 );
				pcm[baseSample * channels + c] = (short)predictor[c];
				o += 4;
			}

			int end = (b + 1) * blockAlign;
			for ( int group = 0; o + 4 * channels <= end; group++ )
			{
				for ( int c = 0; c < channels; c++ )
				{
					for ( int i = 0; i < 8; i++ )
					{
						int nibble = (data[o + (i >> 1)] >> ((i & 1) * 4)) & 0xF;
						int sample = 1 + group * 8 + i;
						if ( sample >= samplesPerBlock )
							break;

						pcm[(baseSample + sample) * channels + c] = DecodeNibble( nibble, ref predictor[c], ref index[c] );
					}
					o += 4;
				}
			}
		}

		var bytes = new byte[pcm.Length * 2];
		for ( int i = 0; i < pcm.Length; i++ )
		{
			bytes[i * 2] = (byte)pcm[i];
			bytes[i * 2 + 1] = (byte)(pcm[i] >> 8);
		}

		return new Pcm( bytes, channels, rate );
	}

	static short DecodeNibble( int nibble, ref int predictor, ref int index )
	{
		int step = StepTable[index];
		int diff = step >> 3;
		if ( (nibble & 1) != 0 ) diff += step >> 2;
		if ( (nibble & 2) != 0 ) diff += step >> 1;
		if ( (nibble & 4) != 0 ) diff += step;
		if ( (nibble & 8) != 0 ) diff = -diff;

		predictor = Math.Clamp( predictor + diff, short.MinValue, short.MaxValue );
		index = Math.Clamp( index + IndexTable[nibble], 0, 88 );
		return (short)predictor;
	}
}
