using System;

namespace Magicka;

/// <summary>A Texture2D or Texture3D XNB file, registered as an s&amp;box texture.</summary>
internal sealed class TextureLoader( string file ) : ResourceLoader<MagickaMount>
{
	protected override object Load()
	{
		var content = XnbContent.Read( Host.ReadAllBytes( file ) );

		return content.Root switch
		{
			Texture2DContent texture => MagickaTextures.Create( Path, texture ),
			Texture3DContent volume => MagickaTextures.CreateVolume( Path, volume ),
			_ => null,
		};
	}
}

/// <summary>Turns XNA 3.1 texture data into s&amp;box textures, keeping DXT blocks compressed.</summary>
internal static class MagickaTextures
{
	public static Texture Create( string name, Texture2DContent texture )
	{
		if ( texture.Width <= 0 || texture.Height <= 0 || texture.Levels.Length == 0 )
			return null;

		var format = ToImageFormat( texture.Format, out var keepsBlocks );
		if ( format is null )
			return null;

		// Compressed XNB levels are separate blocks; the engine derives mips from the base level.
		var data = keepsBlocks ? texture.Levels[0] : ConvertColor( texture.Levels );
		if ( data is null || data.Length == 0 )
			return null;

		return Texture.Create( texture.Width, texture.Height, format.Value )
			.WithName( name )
			.WithMips( keepsBlocks ? 1 : texture.Levels.Length )
			.WithData( data )
			.WithStaticUsage()
			.Finish();
	}

	/// <summary>Particle sheets are slices stacked in a 3D texture.</summary>
	public static Texture CreateVolume( string name, Texture3DContent texture )
	{
		if ( texture.Width <= 0 || texture.Height <= 0 || texture.Depth <= 0 || texture.Levels.Length == 0 )
			return null;

		if ( texture.Format is not (SurfaceFormat.Color or SurfaceFormat.Rgba32) )
			return null;

		return Texture.CreateVolume( texture.Width, texture.Height, texture.Depth, ImageFormat.RGBA8888 )
			.WithName( name )
			.WithMips( texture.Levels.Length )
			.WithData( ConvertColor( texture.Levels ) )
			.WithStaticUsage()
			.Finish();
	}

	private static ImageFormat? ToImageFormat( SurfaceFormat format, out bool keepsBlocks )
	{
		keepsBlocks = true;

		switch ( format )
		{
			case SurfaceFormat.Dxt1:
				return ImageFormat.DXT1;

			case SurfaceFormat.Dxt2:
			case SurfaceFormat.Dxt3:
				return ImageFormat.DXT3;

			case SurfaceFormat.Dxt4:
			case SurfaceFormat.Dxt5:
				return ImageFormat.DXT5;

			case SurfaceFormat.Color:
			case SurfaceFormat.Rgba32:
				keepsBlocks = false;
				return ImageFormat.RGBA8888;

			default:
				return null;
		}
	}

	/// <summary>B, G, R, A to R, G, B, A across all mip levels.</summary>
	private static byte[] ConvertColor( byte[][] levels )
	{
		var data = new byte[levels.Sum( l => l.Length )];
		var offset = 0;

		foreach ( var level in levels )
		{
			Buffer.BlockCopy( level, 0, data, offset, level.Length );
			for ( var i = 0; i + 3 < level.Length; i += 4 )
				(data[offset + i], data[offset + i + 2]) = (data[offset + i + 2], data[offset + i]);

			offset += level.Length;
		}

		return data;
	}
}
