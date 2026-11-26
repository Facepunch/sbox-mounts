namespace Diesel;

/// <summary>Wwise IMA: 36-byte channel blocks, 64 samples, multiply-based nibble expansion.</summary>
static class WwiseAdpcm
{
	static readonly int[] Steps = [7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767];
	static readonly int[] Indices = [-1,-1,-1,-1,2,4,6,8];
	public static byte[] Decode( byte[] bytes )
	{
		var d = new BinaryData( bytes );
		if ( d.UInt( 0 ) != 0x46464952 || d.UInt( 8 ) != 0x45564157 || d.Int( 4 ) != bytes.Length - 8 )
			throw new InvalidDataException( "Invalid Wwise RIFF." );
		int fmt = 0, data = 0, size = 0;
		for ( int p = 12; p < bytes.Length; )
		{
			int length = d.Int( p + 4 ); d.Check( p + 8, length );
			if ( d.UInt( p ) == 0x20746d66 ) fmt = p + 8;
			if ( d.UInt( p ) == 0x61746164 ) { data = p + 8; size = length; }
			p = checked(p + 8 + length + (length & 1));
		}
		int channels = d.UShort( fmt + 2 ), rate = d.Int( fmt + 4 ), block = d.UShort( fmt + 12 );
		if ( fmt != 0 && data != 0 && d.UShort( fmt ) is 1 or 0xfffe && channels is 1 or 2
			&& block == channels * 2 && d.UShort( fmt + 14 ) == 16 && size % block == 0 && rate is >= 3000 and <= 96000 )
		{
			// Wwise's short WAVEFORMATEXTENSIBLE PCM header is not a standard Windows GUID header.
			var wave = new byte[44 + size];
			using var pcmStream = new MemoryStream( wave );
			using var pcmWriter = new BinaryWriter( pcmStream );
			WriteHeader( pcmWriter, channels, rate, size / 2 );
			pcmWriter.Write( bytes, data, size );
			return wave;
		}
		if ( fmt == 0 || data == 0 || d.UShort( fmt ) != 2 || channels is < 1 or > 2
			// Indoor beds include 3 kHz media; the aquarium includes 6 kHz media.
			|| block != 36 * channels || d.UShort( fmt + 14 ) != 4 || size % block != 0 || rate is < 3000 or > 96000 )
			throw new InvalidDataException( $"Unsupported Wwise ADPCM format: codec {d.UShort( fmt ):x4}, channels {channels}, rate {rate}, block {block}, bits {d.UShort( fmt + 14 )}, data {size} bytes." );
		int frames = size / block, samples = checked(frames * 64 * channels);
		var pcm = new short[samples];
		for ( int frame = 0; frame < frames; frame++ )
			for ( int channel = 0; channel < channels; channel++ )
			{
				int p = data + frame * block + channel * 36;
				int history = unchecked((short)d.UShort( p )), step = bytes[p + 2];
				if ( step > 88 ) throw new InvalidDataException( "Invalid ADPCM step." );
				pcm[frame * 64 * channels + channel] = (short)history;
				for ( int s = 1; s < 64; s++ )
				{
					int code = (bytes[p + 4 + (s - 1) / 2] >> (((s - 1) & 1) * 4)) & 15;
					int delta = ((2 * (code & 7) + 1) * Steps[step]) >> 3;
					history = Math.Clamp( history + ((code & 8) != 0 ? -delta : delta), short.MinValue, short.MaxValue );
					step = Math.Clamp( step + Indices[code & 7], 0, 88 );
					pcm[(frame * 64 + s) * channels + channel] = (short)history;
				}
			}
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		WriteHeader( writer, channels, rate, samples );
		foreach ( short sample in pcm ) writer.Write( sample );
		return stream.ToArray();
	}
	static void WriteHeader( BinaryWriter writer, int channels, int rate, int samples )
	{
		writer.Write( 0x46464952 ); writer.Write( 36 + samples * 2 ); writer.Write( 0x45564157 );
		writer.Write( 0x20746d66 ); writer.Write( 16 ); writer.Write( (ushort)1 ); writer.Write( (ushort)channels );
		writer.Write( rate ); writer.Write( rate * channels * 2 ); writer.Write( (ushort)(channels * 2) ); writer.Write( (ushort)16 );
		writer.Write( 0x61746164 ); writer.Write( samples * 2 );
	}
}
