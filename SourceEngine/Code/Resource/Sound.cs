using System;

namespace Source1;

internal sealed class WavSoundLoader( string fullPath ) : ResourceLoader<SourceGameMount>
{
	protected override object Load() => FromWav( Path, Host.GetFileBytes( fullPath ) );

	internal static SoundFile FromWav( string name, byte[] data, int? channel = null )
	{
		var options = new SoundFile.LoadOptions { Channel = channel };
		// Some looping waves store their sample range in a sampler chunk.
		for ( int i = 12; i <= data.Length - 8; )
		{
			int length = BitConverter.ToInt32( data, i + 4 );
			if ( length < 0 || length > data.Length - i - 8 ) break;
			if ( data.AsSpan( i, 4 ).SequenceEqual( "smpl"u8 ) && length >= 60 &&
				BitConverter.ToUInt32( data, i + 36 ) > 0 && BitConverter.ToUInt32( data, i + 48 ) == 0 )
			{
				var start = BitConverter.ToUInt32( data, i + 52 );
				var end = BitConverter.ToUInt32( data, i + 56 );
				if ( start <= end && end < int.MaxValue )
				{
					options.LoopStart = (int)start;
					options.LoopEnd = (int)end + 1;
				}
				break;
			}
			i += 8 + length + (length & 1);
		}
		return SoundFile.FromWav( name, data, options );
	}
}

internal sealed class Mp3SoundLoader( string fullPath ) : ResourceLoader<SourceGameMount>
{
	protected override object Load() => SoundFile.FromMp3( Path, Host.GetFileBytes( fullPath ) );
}

internal static class SourceSoundEvents
{
	internal static SoundEvent Create( SourceGameMount host, SourceSoundDefinition definition, string contentRoot = "", bool ambient = false, int? channel = null )
	{
		var sounds = new List<SoundFile>();
		foreach ( var wave in Application.IsHeadless ? [] : definition.Waves )
		{
			// ^ stores close/far recordings and < stores front/rear recordings,
			// not ordinary left/right stereo. Use close/front until SoundEvent can
			// blend those authored layers by listener distance and source orientation.
			var sound = host.LoadContentSound( wave, contentRoot, channel ?? (definition.Encoding is WaveEncoding.DistanceVariant or WaveEncoding.Directional ? 0 : null) );
			if ( sound is null || !sound.IsValid )
				throw new InvalidOperationException( $"{definition.SourcePath}: wave '{wave}' could not be loaded." );
			sounds.Add( sound );
		}
		var distance = definition.AudibleDistance();
		var frames = definition.SampleFalloff( distance )
			.Select( frame => new Curve.Frame( frame.Time, frame.Value ) { Mode = Curve.HandleMode.Linear } ).ToArray();
		return new SoundEvent
		{
			UI = ambient,
			Sounds = sounds,
			Volume = new RangedFloat( definition.Volume.Min, definition.Volume.Max ),
			Pitch = new RangedFloat( definition.Pitch.Min, definition.Pitch.Max ),
			SelectionMode = SoundEvent.SoundSelectionMode.Random,
			ReverbEnabled = !definition.Dry,
			AirAbsorption = false,
			DistanceAttenuation = !ambient && definition.SoundLevel != 0,
			Distance = distance,
			FalloffMode = SoundFalloffMode.Curve,
			Falloff = new Curve( frames )
		};
	}
}
