namespace Renderware;

sealed class SoundLoaderMp3( string file, bool looping = true ) : ResourceLoader<GTAMount>
{
	protected override object Load()
	{
		using var stream = Host.OpenFile( file ) ?? throw new FileNotFoundException( file );
		using var memory = new MemoryStream();
		stream.CopyTo( memory );
		var data = memory.ToArray();

		return SoundFile.FromMp3( Path, data, new SoundFile.LoadOptions { Loop = looping } );
	}
}

sealed class SoundLoaderWav( string file, bool looping = true ) : ResourceLoader<GTAMount>
{
	protected override object Load()
	{
		using var stream = Host.OpenFile( file ) ?? throw new FileNotFoundException( file );
		using var memory = new MemoryStream();
		stream.CopyTo( memory );
		var data = memory.ToArray();

		var loop = new SoundFile.LoadOptions { Loop = looping };

		if ( !ImaAdpcmWav.IsImaAdpcm( data ) )
			return SoundFile.FromWav( Path, data, loop );

		var pcm = ImaAdpcmWav.Decode( data );
		return SoundFile.FromPcm( Path, pcm.Data, new SoundFile.PcmOptions { Channels = pcm.Channels, Rate = pcm.Rate, Bits = 16, Loop = true } );
	}
}
