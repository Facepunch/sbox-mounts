using System;
using System.Buffers.Binary;
using System.Text;

namespace Magicka;

internal readonly record struct XactWaveEntry(
	int FormatTag,
	int Channels,
	int SampleRate,
	int BitsPerSample,
	int BlockAlign,
	int DataOffset,
	int DataLength );

internal readonly record struct XactWaveRef( string WaveBank, int TrackIndex );

/// <summary>
/// Minimal XACT3 (content version 45/43) WaveBank (.xwb) and SoundBank (.xsb) reader.
/// Reads metadata and cue-to-wave mappings without buffering wave payloads in memory.
/// </summary>
internal static class Xact
{
	public const int FormatPcm = 0;
	public const int FormatAdpcm = 2;

	private static readonly int[] AdpcmAdaptTable =
	[
		230, 230, 230, 230, 307, 409, 512, 614,
		768, 614, 512, 409, 307, 230, 230, 230,
	];

	private static readonly int[] AdpcmCoeff1 = [256, 512, 0, 192, 240, 460, 392];
	private static readonly int[] AdpcmCoeff2 = [0, -256, 0, 64, 0, -208, -232];

	public static XactWaveEntry[] ReadWaveBank( byte[] headerAndMeta, out string bankName )
	{
		bankName = null;
		if ( headerAndMeta.Length < 52 || !headerAndMeta.AsSpan( 0, 4 ).SequenceEqual( "WBND"u8 ) )
			return [];

		var bankOff = BinaryPrimitives.ReadInt32LittleEndian( headerAndMeta.AsSpan( 12 ) );
		var metaOff = BinaryPrimitives.ReadInt32LittleEndian( headerAndMeta.AsSpan( 20 ) );
		var playOff = BinaryPrimitives.ReadInt32LittleEndian( headerAndMeta.AsSpan( 44 ) );

		if ( bankOff + 84 > headerAndMeta.Length )
			return [];

		var flags = BinaryPrimitives.ReadUInt32LittleEndian( headerAndMeta.AsSpan( bankOff ) );
		var count = BinaryPrimitives.ReadInt32LittleEndian( headerAndMeta.AsSpan( bankOff + 4 ) );
		bankName = ReadNullString( headerAndMeta, bankOff + 8, 64 );
		var entrySize = BinaryPrimitives.ReadInt32LittleEndian( headerAndMeta.AsSpan( bankOff + 72 ) );
		var compact = (flags & 0x00020000) != 0;

		if ( compact || count <= 0 || entrySize < 24 || metaOff + (long)count * entrySize > headerAndMeta.Length )
			return [];

		var entries = new XactWaveEntry[count];
		for ( var i = 0; i < count; i++ )
		{
			var eoff = metaOff + i * entrySize;
			var fmt = BinaryPrimitives.ReadUInt32LittleEndian( headerAndMeta.AsSpan( eoff + 4 ) );
			var offset = BinaryPrimitives.ReadInt32LittleEndian( headerAndMeta.AsSpan( eoff + 8 ) );
			var length = BinaryPrimitives.ReadInt32LittleEndian( headerAndMeta.AsSpan( eoff + 12 ) );

			var formatTag = (int)(fmt & 3);
			var channels = (int)((fmt >> 2) & 7);
			var sampleRate = (int)((fmt >> 5) & 0x3FFFF);
			var rawAlign = (int)((fmt >> 23) & 0xFF);
			var bits = ((fmt >> 31) & 1) != 0 ? 16 : 8;
			var blockAlign = formatTag == FormatAdpcm ? (rawAlign + 22) * channels : rawAlign;

			entries[i] = new XactWaveEntry( formatTag, channels, sampleRate, bits, blockAlign, playOff + offset, length );
		}

		return entries;
	}

	/// <summary>Returns the number of bytes from start of an .xwb needed to cover its header and entry metadata.</summary>
	public static int WaveBankMetadataSize( ReadOnlySpan<byte> header52 )
	{
		if ( header52.Length < 52 || !header52[..4].SequenceEqual( "WBND"u8 ) )
			return 0;

		var metaOff = BinaryPrimitives.ReadInt32LittleEndian( header52[20..] );
		var metaLen = BinaryPrimitives.ReadInt32LittleEndian( header52[24..] );
		return metaOff + metaLen;
	}

	/// <summary>Parses an XACT3 .xsb file into cue name → referenced wave tracks.</summary>
	public static Dictionary<string, XactWaveRef[]> ReadSoundBank( byte[] s )
	{
		var cues = new Dictionary<string, XactWaveRef[]>( StringComparer.OrdinalIgnoreCase );
		if ( s.Length < 138 || !s.AsSpan( 0, 4 ).SequenceEqual( "SDBK"u8 ) )
			return cues;

		var nSimple = BinaryPrimitives.ReadUInt16LittleEndian( s.AsSpan( 19 ) );
		var nComplex = BinaryPrimitives.ReadUInt16LittleEndian( s.AsSpan( 21 ) );
		var nWaveBanks = s[27];
		var cueNameLen = BinaryPrimitives.ReadUInt16LittleEndian( s.AsSpan( 30 ) );
		var simpleOff = BinaryPrimitives.ReadInt32LittleEndian( s.AsSpan( 34 ) );
		var complexOff = BinaryPrimitives.ReadInt32LittleEndian( s.AsSpan( 38 ) );
		var cueNamesOff = BinaryPrimitives.ReadInt32LittleEndian( s.AsSpan( 42 ) );
		var wbNamesOff = BinaryPrimitives.ReadInt32LittleEndian( s.AsSpan( 58 ) );

		var waveBanks = new string[nWaveBanks];
		for ( var i = 0; i < nWaveBanks; i++ )
			waveBanks[i] = ReadNullString( s, wbNamesOff + i * 64, 64 );

		var names = ReadCueNames( s, cueNamesOff, cueNameLen );

		for ( var i = 0; i < nSimple && i < names.Count; i++ )
		{
			var off = simpleOff + i * 5;
			if ( off + 5 > s.Length )
				break;

			var soundOff = BinaryPrimitives.ReadInt32LittleEndian( s.AsSpan( off + 1 ) );
			var waves = new List<XactWaveRef>();
			ReadSoundWaves( s, soundOff, waveBanks, waves );
			if ( waves.Count > 0 )
				cues[names[i]] = waves.Distinct().ToArray();
		}

		for ( var i = 0; i < nComplex && nSimple + i < names.Count; i++ )
		{
			var off = complexOff + i * 15;
			if ( off + 5 > s.Length )
				break;

			var flags = s[off];
			var targetOff = BinaryPrimitives.ReadInt32LittleEndian( s.AsSpan( off + 1 ) );
			var waves = new List<XactWaveRef>();

			if ( (flags & 4) != 0 )
			{
				ReadSoundWaves( s, targetOff, waveBanks, waves );
			}
			else if ( targetOff + 8 <= s.Length )
			{
				var varCount = BinaryPrimitives.ReadUInt16LittleEndian( s.AsSpan( targetOff ) );
				var varFlags = BinaryPrimitives.ReadUInt16LittleEndian( s.AsSpan( targetOff + 2 ) );
				var varType = (varFlags >> 3) & 7;
				var vp = targetOff + 8;

				for ( var v = 0; v < varCount; v++ )
				{
					if ( varType == 0 && vp + 5 <= s.Length )
					{
						AddWave( s, vp, waveBanks, waves );
						vp += 5;
					}
					else if ( varType == 1 && vp + 6 <= s.Length )
					{
						ReadSoundWaves( s, BinaryPrimitives.ReadInt32LittleEndian( s.AsSpan( vp ) ), waveBanks, waves );
						vp += 6;
					}
					else if ( varType == 3 && vp + 16 <= s.Length )
					{
						ReadSoundWaves( s, BinaryPrimitives.ReadInt32LittleEndian( s.AsSpan( vp ) ), waveBanks, waves );
						vp += 16;
					}
					else if ( varType == 4 && vp + 3 <= s.Length )
					{
						AddWave( s, vp, waveBanks, waves );
						vp += 3;
					}
					else
					{
						break;
					}
				}
			}

			if ( waves.Count > 0 )
				cues[names[nSimple + i]] = waves.Distinct().ToArray();
		}

		return cues;
	}

	/// <summary>Wraps raw uncompressed PCM bytes in a standard 44-byte RIFF/WAVE header.</summary>
	public static byte[] WrapPcmAsWav( ReadOnlySpan<byte> pcm, int channels, int sampleRate, int bitsPerSample )
	{
		var wav = new byte[44 + pcm.Length];
		var span = wav.AsSpan();
		var blockAlign = (short)(channels * (bitsPerSample / 8));
		var byteRate = sampleRate * blockAlign;

		"RIFF"u8.CopyTo( span );
		BinaryPrimitives.WriteInt32LittleEndian( span[4..], 36 + pcm.Length );
		"WAVE"u8.CopyTo( span[8..] );
		"fmt "u8.CopyTo( span[12..] );
		BinaryPrimitives.WriteInt32LittleEndian( span[16..], 16 );
		BinaryPrimitives.WriteInt16LittleEndian( span[20..], 1 ); // PCM
		BinaryPrimitives.WriteInt16LittleEndian( span[22..], (short)channels );
		BinaryPrimitives.WriteInt32LittleEndian( span[24..], sampleRate );
		BinaryPrimitives.WriteInt32LittleEndian( span[28..], byteRate );
		BinaryPrimitives.WriteInt16LittleEndian( span[32..], blockAlign );
		BinaryPrimitives.WriteInt16LittleEndian( span[34..], (short)bitsPerSample );
		"data"u8.CopyTo( span[36..] );
		BinaryPrimitives.WriteInt32LittleEndian( span[40..], pcm.Length );
		pcm.CopyTo( span[44..] );

		return wav;
	}

	/// <summary>Decodes XACT3 MS-ADPCM (mono or stereo) into 16-bit signed interleaved PCM.</summary>
	public static byte[] DecodeAdpcmToPcm( ReadOnlySpan<byte> adpcm, int channels, int blockAlign )
	{
		if ( channels is not (1 or 2) || blockAlign <= 7 * channels || adpcm.Length < blockAlign )
			return [];

		var blockCount = adpcm.Length / blockAlign;
		var headerSize = 7 * channels;
		var samplesPerBlock = 2 + (blockAlign - headerSize) * 2 / channels;
		var pcm = new byte[blockCount * samplesPerBlock * channels * 2];
		var dst = 0;

		Span<int> predictor = stackalloc int[2];
		Span<int> delta = stackalloc int[2];
		Span<int> sample1 = stackalloc int[2];
		Span<int> sample2 = stackalloc int[2];
		Span<int> coeff1 = stackalloc int[2];
		Span<int> coeff2 = stackalloc int[2];

		void WriteSample( int s )
		{
			var clamped = (short)Math.Clamp( s, short.MinValue, short.MaxValue );
			BinaryPrimitives.WriteInt16LittleEndian( pcm.AsSpan( dst ), clamped );
			dst += 2;
		}

		for ( var b = 0; b < blockCount; b++ )
		{
			var block = adpcm.Slice( b * blockAlign, blockAlign );
			var pos = 0;

			for ( var c = 0; c < channels; c++ )
			{
				predictor[c] = Math.Clamp( (int)block[pos++], 0, 6 );
				coeff1[c] = AdpcmCoeff1[predictor[c]];
				coeff2[c] = AdpcmCoeff2[predictor[c]];
			}

			for ( var c = 0; c < channels; c++ )
			{
				delta[c] = BinaryPrimitives.ReadInt16LittleEndian( block.Slice( pos, 2 ) );
				pos += 2;
			}

			for ( var c = 0; c < channels; c++ )
			{
				sample1[c] = BinaryPrimitives.ReadInt16LittleEndian( block.Slice( pos, 2 ) );
				pos += 2;
			}

			for ( var c = 0; c < channels; c++ )
			{
				sample2[c] = BinaryPrimitives.ReadInt16LittleEndian( block.Slice( pos, 2 ) );
				pos += 2;
			}

			// First two decoded samples per channel are sample2 then sample1.
			for ( var c = 0; c < channels; c++ )
				WriteSample( sample2[c] );
			for ( var c = 0; c < channels; c++ )
				WriteSample( sample1[c] );

			var ch = 0;
			while ( pos < blockAlign )
			{
				var packed = block[pos++];
				for ( var n = 0; n < 2; n++ )
				{
					var nibble = n == 0 ? (packed >> 4) & 0xF : packed & 0xF;
					var signedNibble = nibble >= 8 ? nibble - 16 : nibble;

					var pred = (sample1[ch] * coeff1[ch] + sample2[ch] * coeff2[ch]) / 256 + signedNibble * delta[ch];
					pred = Math.Clamp( pred, short.MinValue, short.MaxValue );

					sample2[ch] = sample1[ch];
					sample1[ch] = pred;

					delta[ch] = Math.Max( 16, (AdpcmAdaptTable[nibble] * delta[ch]) / 256 );
					WriteSample( pred );

					if ( channels == 2 )
						ch ^= 1;
				}
			}
		}

		return pcm;
	}

	private static void ReadSoundWaves( byte[] s, int soff, string[] waveBanks, List<XactWaveRef> waves )
	{
		if ( soff < 0 || soff + 10 > s.Length )
			return;

		var flags = s[soff];
		var p = soff + 9;
		if ( (flags & 1) == 0 )
		{
			if ( p + 3 <= s.Length )
				AddWave( s, p, waveBanks, waves );
			return;
		}

		var clipCount = s[p++];
		if ( (flags & 0x0E) != 0 && p + 2 <= s.Length )
			p += BinaryPrimitives.ReadUInt16LittleEndian( s.AsSpan( p ) );
		if ( (flags & 0x10) != 0 && p + 2 <= s.Length )
			p += BinaryPrimitives.ReadUInt16LittleEndian( s.AsSpan( p ) );

		for ( var c = 0; c < clipCount && p + 9 <= s.Length; c++ )
		{
			var coff = BinaryPrimitives.ReadInt32LittleEndian( s.AsSpan( p + 1 ) );
			p += 9;
			if ( coff < 0 || coff >= s.Length )
				continue;

			var evCount = s[coff];
			var ep = coff + 1;
			for ( var e = 0; e < evCount && ep + 6 <= s.Length; e++ )
			{
				var evId = BinaryPrimitives.ReadUInt32LittleEndian( s.AsSpan( ep ) ) & 0x1F;
				ep += 6; // info(4) + randomOffset(2)

				switch ( evId )
				{
					case 1:
						ep += 2;
						if ( ep + 3 <= s.Length )
							AddWave( s, ep, waveBanks, waves );
						ep += 8;
						break;

					case 3:
						ep += 7;
						if ( ep + 8 > s.Length )
							return;
						ReadTrackVariations( s, ref ep, waveBanks, waves );
						break;

					case 4:
						ep += 2;
						if ( ep + 3 <= s.Length )
							AddWave( s, ep, waveBanks, waves );
						ep += 32;
						break;

					case 6:
						ep += 7 + 24;
						if ( ep + 8 > s.Length )
							return;
						ReadTrackVariations( s, ref ep, waveBanks, waves );
						break;

					default:
						return;
				}
			}
		}
	}

	private static void ReadTrackVariations( byte[] s, ref int ep, string[] waveBanks, List<XactWaveRef> waves )
	{
		var count = BinaryPrimitives.ReadUInt16LittleEndian( s.AsSpan( ep ) );
		ep += 8;
		for ( var i = 0; i < count && ep + 5 <= s.Length; i++ )
		{
			AddWave( s, ep, waveBanks, waves );
			ep += 5;
		}
	}

	private static void AddWave( byte[] s, int offset, string[] waveBanks, List<XactWaveRef> waves )
	{
		var track = BinaryPrimitives.ReadUInt16LittleEndian( s.AsSpan( offset ) );
		var wb = s[offset + 2];
		if ( wb < waveBanks.Length )
			waves.Add( new XactWaveRef( waveBanks[wb], track ) );
	}

	private static string ReadNullString( byte[] data, int offset, int maxLen )
	{
		if ( offset < 0 || offset >= data.Length )
			return "";

		var end = Math.Min( offset + maxLen, data.Length );
		var len = 0;
		while ( offset + len < end && data[offset + len] != 0 )
			len++;

		return Encoding.ASCII.GetString( data, offset, len );
	}

	private static List<string> ReadCueNames( byte[] data, int offset, int length )
	{
		var result = new List<string>();
		if ( offset < 0 || offset + length > data.Length )
			return result;

		var pos = offset;
		var end = offset + length;
		while ( pos < end )
		{
			var start = pos;
			while ( pos < end && data[pos] != 0 )
				pos++;

			if ( pos > start )
				result.Add( Encoding.ASCII.GetString( data, start, pos - start ) );

			pos++;
		}

		return result;
	}
}
