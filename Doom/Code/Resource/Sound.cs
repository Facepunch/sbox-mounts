using Doom.Wad;

namespace Doom;

public sealed class SoundLoader( DmxSound sound ) : ResourceLoader<GameMount>
{
	protected override object Load() => SoundFile.FromWav( Path, sound.ToWav(), new SoundFile.LoadOptions { Loop = false } );
}

/// <summary>A sound event wrapping one DMX sound, so components can play it.</summary>
public sealed class SoundEventLoader( string soundFile ) : ResourceLoader<GameMount>
{
	protected override object Load() => new SoundEvent { Sounds = [SoundFile.Load( soundFile )] };
}
