using Sandbox.Mounting;
using System.IO;

namespace Overgrowth;

public class SoundLoader( string filePath ) : ResourceLoader<GameMount>
{
	internal string LipSyncPath => System.IO.Path.ChangeExtension( filePath, ".txt" );
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
