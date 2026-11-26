using System;

namespace Magicka;

/// <summary>Loads one PCM wave entry lazily from an XACT3 .xwb file as an s&amp;box <see cref="SoundFile"/>.</summary>
internal sealed class SoundWaveLoader( string bankFile, XactWaveEntry entry ) : ResourceLoader<MagickaMount>
{
	protected override object Load()
	{
		var raw = Host.ReadSlice( bankFile, entry.DataOffset, entry.DataLength );
		if ( raw is null || raw.Length == 0 )
			return null;

		byte[] pcm;
		int bits;
		if ( entry.FormatTag == Xact.FormatAdpcm )
		{
			pcm = Xact.DecodeAdpcmToPcm( raw, entry.Channels, entry.BlockAlign );
			bits = 16;
		}
		else
		{
			pcm = raw;
			bits = entry.BitsPerSample;
		}

		if ( pcm.Length == 0 )
			return null;

		var wav = Xact.WrapPcmAsWav( pcm, entry.Channels, entry.SampleRate, bits );
		return SoundFile.FromWav( Path, wav );
	}
}

/// <summary>Builds runtime <see cref="SoundEvent"/>s from mounted Magicka wave paths.</summary>
internal static class MagickaSounds
{
	public static SoundEvent CreateEvent( string[] wavePaths, float volume = 0.85f, float distance = 1200f )
	{
		if ( wavePaths is not { Length: > 0 } )
			return null;

		var sounds = new List<SoundFile>( wavePaths.Length );
		foreach ( var path in wavePaths )
		{
			if ( string.IsNullOrEmpty( path ) )
				continue;

			var file = SoundFile.Load( path );
			if ( file is not null && file.IsValid )
				sounds.Add( file );
		}

		if ( sounds.Count == 0 )
			return null;

		return new SoundEvent
		{
			Sounds = sounds,
			Volume = volume,
			Pitch = new RangedFloat( 0.94f, 1.06f ),
			SelectionMode = SoundEvent.SoundSelectionMode.Random,
			DistanceAttenuation = true,
			Distance = distance,
		};
	}
}
