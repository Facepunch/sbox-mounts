using Sandbox.Mounting;
using System;
using System.Buffers.Binary;
using Halo2.Blam;

namespace Halo2;

/// <summary>
/// Loads a Halo 2 bitmap (bitm) tag as a Texture.
/// </summary>
public class Halo2TextureLoader( MapFile mapFile, TagEntry tag ) : ResourceLoader<Halo2Mount>
{
	protected override object Load()
	{
		var data = BitmapTag.Read( mapFile, tag );
		if ( data is null || data.Width <= 0 || data.Height <= 0 || data.Lod0Size <= 0 )
			return Texture.White;

		var format = GetImageFormat( data.Format );
		if ( format is null || data.BitmapType is not (0 or 1 or 2) || data.IsSwizzled )
			return Texture.White;

		int depth = data.BitmapType == 1 ? Math.Max( 1, (int)data.Depth ) : 1;
		int faces = data.BitmapType == 2 ? 6 : 1;
		int levels = Math.Clamp( data.MipmapCount + 1, 1, 1 + (int)Math.Log2( Math.Max( depth, Math.Max( data.Width, data.Height ) ) ) );
		Span<int> sizes = stackalloc int[levels];
		int length = 0;
		for ( int level = 0; level < levels; level++ )
		{
			int width = Math.Max( 1, data.Width >> level );
			int height = Math.Max( 1, data.Height >> level );
			sizes[level] = checked(faces * Math.Max( 1, depth >> level ) * CalculateMipSize( data.Format, width, height ));
			length = checked(length + sizes[level]);
		}

		// Decode directly to the runtime's smallest-first layout, preserving every authored mip.
		var pixels = BitmapTag.ReadPixelData( Host.FileSystem, mapFile, data, sizes, length );

		if ( data.Format == BitmapData.TextureFormats.X8R8G8B8 )
			for ( int i = 3; i < length; i += 4 ) pixels[i] = 255;

		if ( data.Format == BitmapData.TextureFormats.U8V8 )
		{
			var expanded = new byte[checked(length * 4)];
			for ( int i = 0; i < length; i++ )
				BinaryPrimitives.WriteSingleLittleEndian( expanded.AsSpan( i * 4 ), Math.Max( -1f, (sbyte)pixels[i] / 127f ) );
			pixels = expanded;
		}
		else if ( data.Format == BitmapData.TextureFormats.G8B8 )
		{
			var expanded = new byte[checked(length * 2)];
			for ( int i = 0; i < length; i += 2 )
			{
				expanded[i * 2] = pixels[i];
				expanded[i * 2 + 1] = pixels[i + 1];
				expanded[i * 2 + 3] = 255;
			}
			pixels = expanded;
		}
		if ( data.BitmapType == 1 )
			return Texture.CreateVolume( data.Width, data.Height, depth, format.Value ).WithData( pixels ).WithMips( levels ).WithStaticUsage().Finish();

		return faces == 6
			? Texture.CreateCube( data.Width, data.Height, format.Value ).WithData( pixels ).WithMips( levels ).WithStaticUsage().Finish()
			: Texture.Create( data.Width, data.Height, format.Value ).WithData( pixels ).WithMips( levels ).WithStaticUsage().Finish();
	}

	static ImageFormat? GetImageFormat( short format ) => format switch
	{
		BitmapData.TextureFormats.DXT1 => ImageFormat.DXT1,
		BitmapData.TextureFormats.DXT3 => ImageFormat.DXT3,
		BitmapData.TextureFormats.DXT5 => ImageFormat.DXT5,
		BitmapData.TextureFormats.A8R8G8B8 => ImageFormat.BGRA8888,
		BitmapData.TextureFormats.X8R8G8B8 => ImageFormat.BGRA8888,
		BitmapData.TextureFormats.R5G6B5 => ImageFormat.RGB565,
		BitmapData.TextureFormats.U8V8 => ImageFormat.RG3232F,
		BitmapData.TextureFormats.G8B8 => ImageFormat.BGRA8888,
		BitmapData.TextureFormats.A8 => ImageFormat.A8,
		BitmapData.TextureFormats.Y8 => ImageFormat.I8,
		_ => null
	};

	static int CalculateMipSize( short format, int w, int h )
	{
		return format switch
		{
			BitmapData.TextureFormats.DXT1 => Math.Max( 1, (w + 3) / 4 ) * Math.Max( 1, (h + 3) / 4 ) * 8,
			BitmapData.TextureFormats.DXT3 or BitmapData.TextureFormats.DXT5 =>
				Math.Max( 1, (w + 3) / 4 ) * Math.Max( 1, (h + 3) / 4 ) * 16,
			BitmapData.TextureFormats.A8R8G8B8 or BitmapData.TextureFormats.X8R8G8B8 => w * h * 4,
			BitmapData.TextureFormats.U8V8 or BitmapData.TextureFormats.G8B8 or
			BitmapData.TextureFormats.R5G6B5 or BitmapData.TextureFormats.A1R5G5B5 or
			BitmapData.TextureFormats.A4R4G4B4 or BitmapData.TextureFormats.A8Y8 => w * h * 2,
			BitmapData.TextureFormats.A8 or BitmapData.TextureFormats.Y8 or BitmapData.TextureFormats.AY8 => w * h,
			_ => throw new NotSupportedException()
		};
	}

}
