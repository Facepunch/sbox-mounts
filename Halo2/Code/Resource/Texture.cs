using Sandbox.Mounting;
using System;
using Halo2.Blam;

namespace Halo2;

/// <summary>
/// Loads a Halo 2 bitmap (bitm) tag as a Texture.
/// </summary>
public class Halo2TextureLoader( MapFile mapFile, TagEntry tag ) : ResourceLoader<Halo2Mount>
{
	protected override object Load()
	{
		var bitmap = new BitmapTag( mapFile, tag );

		if ( bitmap.Bitmaps.Count == 0 )
			return Texture.White;

		var data = bitmap.Bitmaps[0];
		if ( data.Width == 0 || data.Height == 0 || data.Lod0Size == 0 )
			return Texture.White;

		// Skip cubemaps
		if ( data.BitmapType == 2 )
			return Texture.White;

		var format = GetImageFormat( data.Format );
		if ( format is null ) return Texture.White;

		int baseMipSize = CalculateBaseMipSize( data );
		var pixels = BitmapTag.ReadPixelData( Host.FileSystem, mapFile, data, baseMipSize );
		if ( pixels is null || pixels.Length == 0 ) return Texture.White;
		baseMipSize = Math.Min( baseMipSize, pixels.Length );
		if ( format == ImageFormat.RGBA8888 )
		{
			bool opaque = data.Format == BitmapData.TextureFormats.X8R8G8B8;
			for ( int i = 0; i + 3 < baseMipSize; i += 4 )
			{
				byte alpha = opaque ? (byte)255 : pixels[i];
				pixels[i] = pixels[i + 1];
				pixels[i + 1] = pixels[i + 2];
				pixels[i + 2] = pixels[i + 3];
				pixels[i + 3] = alpha;
			}
		}

		var builder = Texture.Create( data.Width, data.Height, format.Value ).WithData( pixels, baseMipSize );
		return format is ImageFormat.DXT1 or ImageFormat.DXT3 or ImageFormat.DXT5
			? builder.WithMips( 1 ).WithStaticUsage().Finish()
			: builder.WithMips().Finish();
	}

	static ImageFormat? GetImageFormat( short format ) => format switch
	{
		BitmapData.TextureFormats.DXT1 => ImageFormat.DXT1,
		BitmapData.TextureFormats.DXT3 => ImageFormat.DXT3,
		BitmapData.TextureFormats.DXT5 => ImageFormat.DXT5,
		BitmapData.TextureFormats.A8R8G8B8 => ImageFormat.RGBA8888,
		BitmapData.TextureFormats.X8R8G8B8 => ImageFormat.RGBA8888,
		BitmapData.TextureFormats.R5G6B5 => ImageFormat.RGB565,
		BitmapData.TextureFormats.A8 => ImageFormat.A8,
		BitmapData.TextureFormats.Y8 => ImageFormat.I8,
		_ => null
	};

	static int CalculateBaseMipSize( BitmapData data )
	{
		int w = data.Width;
		int h = data.Height;

		return data.Format switch
		{
			BitmapData.TextureFormats.DXT1 => Math.Max( 1, (w + 3) / 4 ) * Math.Max( 1, (h + 3) / 4 ) * 8,
			BitmapData.TextureFormats.DXT3 or BitmapData.TextureFormats.DXT5 =>
				Math.Max( 1, (w + 3) / 4 ) * Math.Max( 1, (h + 3) / 4 ) * 16,
			BitmapData.TextureFormats.A8R8G8B8 or BitmapData.TextureFormats.X8R8G8B8 => w * h * 4,
			BitmapData.TextureFormats.R5G6B5 or BitmapData.TextureFormats.A1R5G5B5 or
			BitmapData.TextureFormats.A4R4G4B4 or BitmapData.TextureFormats.A8Y8 => w * h * 2,
			BitmapData.TextureFormats.A8 or BitmapData.TextureFormats.Y8 or BitmapData.TextureFormats.AY8 => w * h,
			_ => data.Lod0Size
		};
	}

}
