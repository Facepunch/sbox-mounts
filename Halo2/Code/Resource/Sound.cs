using System;
using System.Buffers.Binary;
using System.IO;
using Halo2.Blam;

namespace Halo2;

sealed class Halo2SoundLoader( MapFile map, TagEntry tag, int gestalt ) : Sandbox.Mounting.ResourceLoader<Halo2Mount>
{
	protected override object Load()
	{
		int header = map.VaToOffset( (int)tag.DataVirtualAddress );
		int U16( int p ) => (ushort)map.ReadInt32( p );
		int Table( int block, int index, int stride ) => (uint)index < map.ReadInt32( block )
			? map.VaToOffset( map.ReadInt32( block + 4 ) ) + index * stride : -1;
		int codec = Table( gestalt, map.ReadSpan( header + 3, 1 )[0], 3 );
		if ( codec < 0 ) return null;
		var format = map.ReadSpan( codec, 3 );
		int channels = format[1] + 1, compression = format[2];
		if ( compression is not (1 or 3 or 5) ) return null;
		uint rate = format[0] switch { 0 => 22050, 1 => 44100, 2 => 32000, 3 => 48000, _ => 0 };
		if ( rate == 0 ) return null;
		var sound = new SoundEvent { Sounds = [], SelectionMode = SoundEvent.SoundSelectionMode.RandomExclusive };
		int playback = Table( gestalt + 8, (short)U16( header + 6 ), 56 );
		if ( playback >= 0 )
		{
			float maxDistance = BitConverter.Int32BitsToSingle( map.ReadInt32( playback + 4 ) );
			if ( maxDistance > 0 ) sound.Distance = maxDistance * 120;
			float gainDb = BitConverter.Int32BitsToSingle( map.ReadInt32( playback + 0x10 ) );
			sound.Volume = MathF.Pow( 10, Math.Clamp( gainDb, -64f, 0f ) / 20f );
		}
		int firstRange = U16( header + 8 ), rangeCount = map.ReadSpan( header + 10, 1 )[0];
		int chunkTable = Table( gestalt + 96, 0, 8 ); // English index in the localized chunk tables.
		if ( chunkTable < 0 ) return null;
		for ( int r = 0; r < rangeCount; r++ )
		{
			int range = Table( gestalt + 40, firstRange + r, 12 );
			if ( range < 0 ) continue;
			int first = U16( range + 8 ), count = U16( range + 10 );
			for ( int n = 0; n < count; n++ )
			{
				int permutation = Table( gestalt + 48, first + n, 16 );
				if ( permutation < 0 ) continue;
				int loc = Table( permutation + 8, 0, 8 );
				if ( loc < 0 ) continue;
				string name = $"{Path}/permutation_{first + n}.vsnd";
				int chunkCount = U16( loc + 6 );
				int firstChunk = U16( loc + 4 );
				int last = Table( chunkTable, firstChunk + chunkCount - 1, 16 );
				if ( chunkCount == 0 || last < 0 ) continue;

				// Bit 29 on the final chunk selects the shared bank. Localized offsets use the language bank.
				string bank = (map.ReadInt32( last + 4 ) & (1 << 29)) == 0 ? "en"
					: tag.TagPath.StartsWith( "sound_remastered", StringComparison.OrdinalIgnoreCase ) ? "remastered" : "neutral";
				using var file = Host.FileSystem.OpenRead( $"{map.MapsDirectory}/sounds_{bank}.dat" );
				using var bytes = new MemoryStream();
				for ( int i = 0; i < chunkCount; i++ )
				{
					int chunk = Table( chunkTable, firstChunk + i, 16 );
					if ( chunk < 0 ) break;
					int packed = map.ReadInt32( chunk + 4 );
					int length = packed & 0x03ffffff;
					if ( length == 0 ) continue;
					file.Position = (uint)map.ReadInt32( chunk );
					if ( length > file.Length - file.Position ) throw new InvalidDataException( "Sound chunk exceeds its bank." );
					int offset = checked((int)bytes.Length);
					bytes.SetLength( checked(offset + length) );
					file.ReadExactly( bytes.GetBuffer().AsSpan( offset, length ) );
				}
				if ( bytes.Length == 0 ) continue;
				var data = bytes.GetBuffer().AsMemory( 0, (int)bytes.Length );
				SoundFile fileSound = compression switch
				{
					1 => FromPcm( name, DecodeAdpcm( channels, data.Span ), channels, rate ),
					3 => FromPcm( name, bytes.GetBuffer().AsSpan( 0, data.Length ), channels, rate ),
					5 => DecodeOpus( name, channels, data ),
					_ => null
				};
				if ( fileSound.IsValid() ) sound.Sounds.Add( fileSound );
			}
		}
		return sound.Sounds.Count > 0 ? sound : null;
	}

	static SoundFile DecodeOpus( string path, int channels, ReadOnlyMemory<byte> data )
	{
		var packets = new List<ReadOnlyMemory<byte>>();
		while ( data.Length >= 2 )
		{
			// MCC negates the final packet length in each chunk.
			int size = Math.Abs( (int)BinaryPrimitives.ReadInt16LittleEndian( data.Span ) );
			if ( packets.Count >= 16384 || size == 0 || size > data.Length - 2 ) return null;
			packets.Add( data.Slice( 2, size ) );
			data = data[(size + 2)..];
		}
		if ( !data.IsEmpty || packets.Count == 0 ) return null;

		return SoundFile.FromOpusPackets( path, channels, packets );
	}

	static SoundFile FromPcm( string name, Span<byte> data, int channels, uint rate )
	{
		if ( data.Length % (2 * channels) != 0 ) throw new InvalidDataException( "Incomplete PCM frame." );
		if ( channels == 4 )
		{
			// Fold front and rear pairs into the stereo layout supported by the mixer.
			for ( int offset = 0; offset < data.Length; offset += 8 )
			for ( int side = 0; side < 4; side += 2 )
			{
				int front = BinaryPrimitives.ReadInt16LittleEndian( data[(offset + side)..] );
				int rear = BinaryPrimitives.ReadInt16LittleEndian( data[(offset + side + 4)..] );
				BinaryPrimitives.WriteInt16LittleEndian( data[(offset / 2 + side)..], (short)((front + rear) / 2) );
			}
			data = data[..(data.Length / 2)];
			channels = 2;
		}
		return SoundFile.FromPcm( name, data, new() { Channels = channels, Rate = rate } );
	}

	static readonly short[] Steps = [7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,
		73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,
		876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,
		5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767];
	static readonly int[] IndexChange = [-1,-1,-1,-1,2,4,6,8];

	static byte[] DecodeAdpcm( int channels, ReadOnlySpan<byte> data )
	{
		int stride = 36 * channels;
		if ( data.Length % stride != 0 ) throw new InvalidDataException( "Incomplete Xbox ADPCM block." );
		var samples = new byte[checked(data.Length / stride * 64 * channels * 2)];
		for ( int b = 0; b < data.Length / stride; b++ )
		for ( int ch = 0; ch < channels; ch++ )
		{
			var block = data.Slice( b * stride, stride );
			// Quad blocks store four predictors followed by four step indices.
			int predictor = channels == 4 ? 2 * ch : 4 * ch;
			int indexOffset = channels == 4 ? 8 + 2 * ch : predictor + 2;
			int value = BinaryPrimitives.ReadInt16LittleEndian( block[predictor..] );
			int index = block[indexOffset];
			if ( index > 88 ) throw new InvalidDataException( "Invalid Xbox ADPCM step index." );
			int groupBytes = channels == 4 ? 2 : 4;
			for ( int frame = 0; frame < 64; frame++ )
			{
				BinaryPrimitives.WriteInt16LittleEndian( samples.AsSpan( ((b * 64 + frame) * channels + ch) * 2 ), (short)value );
				int sampleByte = frame / 2;
				int packed = block[channels * 4 + (sampleByte / groupBytes * channels + ch) * groupBytes + sampleByte % groupBytes];
				int code = (packed >> ((frame & 1) * 4)) & 15;
				int step = Steps[index];
				int delta = (step >> 3) + ((code & 4) != 0 ? step : 0) + ((code & 2) != 0 ? step >> 1 : 0) + ((code & 1) != 0 ? step >> 2 : 0);
				value = Math.Clamp( value + ((code & 8) == 0 ? delta : -delta), short.MinValue, short.MaxValue );
				index = Math.Clamp( index + IndexChange[code & 7], 0, 88 );
			}
		}
		return samples;
	}
}
