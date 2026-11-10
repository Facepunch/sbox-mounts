namespace GoldSrc;

class WavSoundLoader( string fullPath ) : ResourceLoader<GameMount>
{
	protected override object Load() => SoundFile.FromWav( Path, Host.FileSystem.ReadAllBytes( fullPath ).ToArray() );
}
