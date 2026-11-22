namespace GoldSrc;

internal sealed class MusicLoader( string path ) : ResourceLoader<GameMount>
{
	protected override object Load() => SoundFile.FromMp3( Path, Host.FileSystem.ReadAllBytes( path ).ToArray() );
}
