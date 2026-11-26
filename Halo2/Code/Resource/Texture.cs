using Sandbox.Mounting;
using System;
using System.Buffers.Binary;
using Halo2.Blam;

namespace Halo2;

/// <summary>
/// Loads a Halo 2 bitmap (bitm) tag as a Texture.
/// </summary>
public class Halo2TextureLoader( MapFile mapFile, TagEntry tag, int bitmapIndex = 0, bool hudMask = false, Rect[][] spriteSequences = null, bool loop = false, (float Fill, int Rgb)? meter = null, (int Dark, int Light, int Accent)? hudGradient = null ) : ResourceLoader<Halo2Mount>
{
	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = ResourceType.Texture;
		if ( spriteSequences is not null || bitmapIndex != 0 ) return null;
		if ( name is "sheet.vtex" or "loop.vtex" ) return SpriteSheet( mapFile, tag, name == "loop.vtex" );
		if ( name.StartsWith( "sequence_", StringComparison.Ordinal ) && name.EndsWith( ".vtex", StringComparison.Ordinal )
			&& int.TryParse( name.AsSpan()[9..^5], out int selected ) && selected >= 0 )
			return SpriteSheet( mapFile, tag, false, selected );
		if ( name.EndsWith( ".vtex", StringComparison.Ordinal ) && int.TryParse( name.AsSpan( 0, name.Length - 5 ), out int index )
			&& index > 0 && BitmapTag.Read( mapFile, tag, index ) is not null )
			return new Halo2TextureLoader( mapFile, tag, index );
		return null;
	}

	internal static Halo2TextureLoader SpriteSheet( MapFile map, TagEntry bitmap, bool loop, int selected = -1 )
	{
		if ( bitmap.DataSize < 76 ) return null;
		int sequences = map.VaToOffset( (int)bitmap.DataVirtualAddress ) + 60;
		int count = map.ReadInt32( sequences );
		if ( count is <= 0 or > 256 ) return null;
		if ( selected >= count ) return null;
		int sequence = map.VaToOffset( map.ReadInt32( sequences + 4 ) );
		if ( selected >= 0 )
		{
			sequence += selected * 60;
			count = 1;
		}
		var atlas = new Rect[count][];
		var pages = new List<int>();
		var framePages = new List<int>();
		for ( int i = 0; i < count; i++, sequence += 60 )
		{
			int frames = map.ReadInt32( sequence + 52 );
			if ( frames is <= 0 or > 1024 ) return null;
			atlas[i] = new Rect[frames];
			int row = map.VaToOffset( map.ReadInt32( sequence + 56 ) );
			for ( int j = 0; j < frames; j++, row += 32 )
			{
				int frameImage = (short)map.ReadInt32( row );
				if ( frameImage < 0 ) return null;
				if ( !pages.Contains( frameImage ) ) pages.Add( frameImage );
				framePages.Add( pages.IndexOf( frameImage ) );
				atlas[i][j] = SpriteBounds( map, row + 8 );
			}
		}
		var loader = new Halo2TextureLoader( map, bitmap, pages[0], spriteSequences: atlas, loop: loop );
		if ( pages.Count == 1 ) return loader;
		loader.sheetPages = pages.ToArray();
		int frame = 0;
		foreach ( var sequenceFrames in atlas )
			for ( int i = 0; i < sequenceFrames.Length; i++ )
			{
				var rect = sequenceFrames[i];
				sequenceFrames[i] = new Rect( rect.Left, (framePages[frame++] + rect.Top) / pages.Count, rect.Width, rect.Height / pages.Count );
			}
		return loader;
	}

	internal static Rect SpriteBounds( MapFile map, int offset )
	{
		var data = map.ReadSpan( offset, 16 );
		float left = BinaryPrimitives.ReadSingleLittleEndian( data ), right = BinaryPrimitives.ReadSingleLittleEndian( data[4..] );
		float top = BinaryPrimitives.ReadSingleLittleEndian( data[8..] ), bottom = BinaryPrimitives.ReadSingleLittleEndian( data[12..] );
		return new Rect( left, top, right - left, bottom - top );
	}

	int[] sheetPages;

	Texture LoadPagedSheet( BitmapData first, ImageFormat format, ReadOnlySpan<int> sizes, int length )
	{
		// Stack whole compressed block rows so sampling retains the authored pixels.
		int levels = sizes.Length;
		if ( format is not (ImageFormat.DXT1 or ImageFormat.DXT3 or ImageFormat.DXT5) ) return null;
		while ( levels > 0 && first.Height % (4 << (levels - 1)) != 0 ) levels--;
		if ( levels == 0 ) return null;
		int mipBytes = 0;
		foreach ( int size in sizes[..levels] ) mipBytes += size;
		var atlas = new byte[checked(mipBytes * sheetPages.Length)];
		var pixels = new byte[length];
		for ( int i = 0; i < sheetPages.Length; i++ )
		{
			var page = i == 0 ? first : BitmapTag.Read( mapFile, tag, sheetPages[i] );
			if ( page is null || page.Width != first.Width || page.Height != first.Height ||
				page.Format != first.Format || page.BitmapType != 0 || page.IsSwizzled || page.MipmapCount != first.MipmapCount ) return null;
			BitmapTag.ReadPixelData( Host.FileSystem, mapFile, page, sizes, pixels );
			int source = length, destination = atlas.Length;
			foreach ( int size in sizes[..levels] )
			{
				source -= size;
				destination -= size * sheetPages.Length;
				pixels.AsSpan( source, size ).CopyTo( atlas.AsSpan( destination + i * size, size ) );
			}
		}
		var texture = Texture.Create( first.Width, first.Height * sheetPages.Length, format ).WithData( atlas ).WithMips( levels ).WithStaticUsage();
		foreach ( var frames in spriteSequences )
			texture = texture.AddSequence( frames, frames.Length, loop );
		return texture.Finish();
	}

	protected override object Load()
	{
		var data = BitmapTag.Read( mapFile, tag, bitmapIndex );
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

		if ( sheetPages is not null )
			return LoadPagedSheet( data, format.Value, sizes, length );

		// Decode directly to the runtime's smallest-first layout, preserving every authored mip.
		var pixels = BitmapTag.ReadPixelData( Host.FileSystem, mapFile, data, sizes, length );

		if ( hudGradient is {} gradient && data.Format == BitmapData.TextureFormats.A8R8G8B8 )
		{
			// The HUD pass uses green to sample the gradient and blue to blend in the highlight.
			for ( int i = 0; i < length; i += 4 )
			{
				int green = pixels[i + 1], blue = pixels[i];
				for ( int channel = 0; channel < 3; channel++ )
				{
					int shift = channel * 8;
					int dark = (byte)(gradient.Dark >> shift), light = (byte)(gradient.Light >> shift);
					int accent = (byte)(gradient.Accent >> shift);
					int ramp = dark * (255 - green) + light * green;
					const int denominator = 255 * 255;
					int blended = ramp * (255 - blue) + accent * blue * 255;
					pixels[i + channel] = (byte)((blended + denominator / 2) / denominator);
				}
			}
		}

		if ( (hudMask || meter.HasValue) && data.Format == BitmapData.TextureFormats.A8R8G8B8 )
		{
			int tint = meter?.Rgb ?? 0xffffff;
			for ( int i = 0; i < length; i += 4 )
			{
				if ( meter is {} state && pixels[i] > state.Fill * 255 ) pixels[i + 3] = 0;
				pixels[i] = (byte)tint;
				pixels[i + 1] = (byte)(tint >> 8);
				pixels[i + 2] = (byte)(tint >> 16);
			}
		}

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

		if ( faces == 6 )
			return Texture.CreateCube( data.Width, data.Height, format.Value ).WithData( pixels ).WithMips( levels ).WithStaticUsage().Finish();

		var texture = Texture.Create( data.Width, data.Height, format.Value ).WithData( pixels ).WithMips( levels ).WithStaticUsage();
		if ( spriteSequences is not null )
			foreach ( var frames in spriteSequences )
				texture = texture.AddSequence( frames, frames.Length, loop );
		return texture.Finish();
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
