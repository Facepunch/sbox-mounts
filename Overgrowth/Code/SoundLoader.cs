using Sandbox.Mounting;
using System;
using System.Buffers.Binary;
using System.IO;

namespace Overgrowth;

public class SoundLoader( string filePath ) : ResourceLoader<GameMount>
{
	internal string LipSyncPath => System.IO.Path.ChangeExtension( filePath, ".txt" );
	SoundFile distantSound;
	internal SoundFile DistantSound => distantSound ??= LoadDistantSound();

	SoundFile LoadDistantSound()
	{
		var wave = Host.FileSystem.ReadAllBytes( filePath ).ToArray();
		var filter = Host.FileSystem.ReadAllBytes( "Data/Sounds/filters/dist_lowpass.wav" ).ToArray();
		if ( !ApplyDistanceFilter( wave, filter ) ) return null;
		return SoundFile.FromWav( System.IO.Path.ChangeExtension( Path, "distant.vsnd" ), wave );
	}

	// SimpleFIRFilter centers the authored kernel on each frame, padding both ends with silence.
	static bool ApplyDistanceFilter( byte[] wave, byte[] filter )
	{
		var samples = Pcm16( wave, out int channels );
		var taps = Pcm16( filter, out int filterChannels );
		if ( samples.IsEmpty || taps.IsEmpty || filterChannels != 1 ) return false;
		var coefficients = new float[taps.Length / 2];
		for ( int i = 0; i < coefficients.Length; ++i )
			coefficients[i] = BinaryPrimitives.ReadInt16LittleEndian( taps[(i * 2)..] ) / 32767f;

		int frames = samples.Length / (2 * channels);
		var input = new short[(frames + coefficients.Length) * channels];
		int padding = coefficients.Length / 2 * channels;
		for ( int i = 0; i < frames * channels; ++i )
			input[padding + i] = BinaryPrimitives.ReadInt16LittleEndian( samples[(i * 2)..] );
		for ( int i = 0; i < frames * channels; ++i )
		{
			float sum = 0f;
			for ( int j = 0; j < coefficients.Length; ++j )
				sum += input[i + j * channels] * coefficients[j];
			BinaryPrimitives.WriteInt16LittleEndian( samples[(i * 2)..], (short)sum );
		}
		return true;
	}

	static Span<byte> Pcm16( Span<byte> wave, out int channels )
	{
		channels = 0;
		if ( wave.Length < 12 || !wave[..4].SequenceEqual( "RIFF"u8 ) || !wave[8..12].SequenceEqual( "WAVE"u8 ) ) return default;
		var chunks = wave[12..];
		while ( chunks.Length >= 8 )
		{
			int size = BinaryPrimitives.ReadInt32LittleEndian( chunks[4..] );
			if ( size < 0 || size > chunks.Length - 8 ) break;
			var data = chunks.Slice( 8, size );
			if ( chunks[..4].SequenceEqual( "fmt "u8 ) && size >= 16 )
			{
				if ( BinaryPrimitives.ReadUInt16LittleEndian( data ) != 1 || BinaryPrimitives.ReadUInt16LittleEndian( data[14..] ) != 16 ) break;
				channels = BinaryPrimitives.ReadUInt16LittleEndian( data[2..] );
			}
			if ( chunks[..4].SequenceEqual( "data"u8 ) && channels > 0 && size % (2 * channels) == 0 ) return data;
			if ( size == chunks.Length - 8 ) break;
			chunks = chunks[(8 + size + (size & 1))..];
		}
		return default;
	}

	protected override object Load()
	{
		var bytes = Host.FileSystem.ReadAllBytes( filePath ).ToArray();
		return System.IO.Path.GetExtension( filePath ).ToLowerInvariant() switch
		{
			".wav" => SoundFile.FromWav( Path, bytes, new SoundFile.LoadOptions { Loop = System.IO.Path.GetFileName( filePath ).Equals( "character_on_fire_loop_small.wav", System.StringComparison.OrdinalIgnoreCase ) } ),
			".ogg" => SoundFile.FromOgg( Path, bytes ),
			".mp3" => SoundFile.FromMp3( Path, bytes ),
			_ => throw new InvalidDataException( $"Unsupported Overgrowth sound: {filePath}" )
		};
	}
}
