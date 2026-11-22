namespace GoldSrc;

class WavSoundLoader( string fullPath ) : ResourceLoader<GameMount>
{
	protected override object Load() => SoundFile.FromWav( Path, Host.FileSystem.ReadAllBytes( fullPath ).ToArray() );
}

internal static class GoldSrcSoundEvents
{
	private static readonly Curve LinearFalloff = new(
		new Curve.Frame( 0f, 1f ) { Mode = Curve.HandleMode.Linear },
		new Curve.Frame( 1f, 0f ) );

	internal const float Normal = 0.8f;
	internal const float Idle = 2f;

	static float Range( float attenuation ) => attenuation > 0 ? 1000f / attenuation : float.MaxValue;

	internal static SoundHandle Play( SoundFile sound, Vector3 position, float volume = 1, float pitch = 1, float attenuation = Normal )
	{
		if ( sound is null ) return null;
		var handle = Sound.PlayFile( sound, volume, pitch );
		if ( handle.IsValid() )
		{
			handle.Position = position;
			handle.DistanceAttenuation = attenuation > 0;
			handle.Distance = Range( attenuation );
			handle.Falloff = LinearFalloff;
			handle.OcclusionEnabled = false;
			handle.AirAbsorption = false;
		}
		return handle;
	}

	internal static SoundEvent Create( SoundFile[] sounds, float volume, float attenuation )
	{
		var attenuated = attenuation > 0f;
		return new SoundEvent
		{
			Sounds = [.. sounds],
			SelectionMode = SoundEvent.SoundSelectionMode.Random,
			Volume = volume,
			DistanceAttenuation = attenuated,
			Distance = Range( attenuation ),
			FalloffMode = SoundFalloffMode.Curve,
			Falloff = LinearFalloff,
			OcclusionEnabled = false,
			AirAbsorption = false
		};
	}
}
