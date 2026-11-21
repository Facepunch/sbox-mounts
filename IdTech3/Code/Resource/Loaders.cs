using IdTech3.Formats;

namespace IdTech3;

/// <summary>An image (tga, jpg, png, dds, iwi).</summary>
sealed class TextureLoader( GameData game, string path ) : ResourceLoader<IdTech3Mount>
{
	protected override object Load() => game.GetTexture( path );
}

/// <summary>A shader script entry or implicit image shader, as a model-lit material.</summary>
sealed class MaterialLoader( GameData game, string shader ) : ResourceLoader<IdTech3Mount>
{
	protected override object Load() => game.GetModelMaterial( shader );
}

/// <summary>env/NAME_* six-sided skybox as a sky material (for SkyBox2D).</summary>
sealed class SkyboxLoader( GameData game, string box ) : ResourceLoader<IdTech3Mount>
{
	protected override object Load()
	{
		var cube = game.GetSkyCube( box );
		if ( cube is null ) return null;
		var material = Material.Create( Path, "sky" );
		material?.Set( "SkyTexture", cube );
		return material;
	}
}

/// <summary>wav (PCM, IMA/MS ADPCM, float, MP3-in-WAV), mp3 and ogg.</summary>
sealed class SoundLoader( GameData game, string path, bool loop ) : ResourceLoader<IdTech3Mount>
{
	protected override object Load() => SoundUtil.Load( game, path, Path, loop );
}

static class SoundUtil
{
	public static SoundFile Load( GameData game, string path, string name, bool loop )
	{
		var data = game.Read( path );
		if ( data is null || data.Length == 0 ) return null;

		switch ( PathUtil.Extension( path ) )
		{
			case ".mp3": return SoundFile.FromMp3( name, data, loop );
			case ".ogg": return SoundFile.FromOgg( name, data, new SoundFile.LoadOptions { Loop = loop } );
		}

		var info = Wav.Parse( data );
		if ( info is null ) return null;

		switch ( info.Encoding )
		{
			case WavEncoding.Pcm:
				return SoundFile.FromWav( name, data, loop );

			case WavEncoding.Mp3:
				return SoundFile.FromMp3( name, data.AsSpan( info.DataOffset, info.DataLength ), loop );

			case WavEncoding.ImaAdpcm:
			case WavEncoding.MsAdpcm:
			case WavEncoding.Float:
			{
				var pcm = Wav.DecodeToPcm16( data, info );
				if ( pcm is null || pcm.Length == 0 ) return null;
				return SoundFile.FromPcm( name, pcm, info.Channels, (uint)info.SampleRate, 16, loop );
			}

			default:
				return null;
		}
	}
}
