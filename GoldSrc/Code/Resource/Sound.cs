using Sandbox.Mounting;
using System.IO;

namespace GoldSrc;

public class WavSoundLoader( string fullPath ) : ResourceLoader<GameMount>
{
	private string FullPath { get; init; } = fullPath;

	protected override object Load()
	{
		return SoundFile.FromWav( Path, Host.FileSystem.ReadAllBytes( FullPath ).ToArray() );
	}
}

internal static class GoldSrcSoundEvents
{
	private static readonly Curve LinearFalloff = new(
		new Curve.Frame( 0f, 1f, 0f, -1f ),
		new Curve.Frame( 1f, 0f, -1f, 0f ) );

	internal static SoundEvent Create( SoundFile[] sounds, float volume, float attenuation )
	{
		var attenuated = attenuation > 0f;
		return new SoundEvent
		{
			Sounds = [.. sounds],
			SelectionMode = SoundEvent.SoundSelectionMode.Random,
			Volume = volume,
			DistanceAttenuation = attenuated,
			Distance = attenuated ? 1000f / attenuation : float.MaxValue,
			Falloff = LinearFalloff,
			OcclusionEnabled = false,
			ReverbEnabled = false
		};
	}
}
