using System;

namespace Doom.Wad;

/// <summary>256 colours. A WAD's PLAYPAL lump holds several of these; index 0 is the normal one.</summary>
public sealed record Palette( Color32[] Colors );

/// <summary>
/// A paletted image: one palette index per pixel, row by row, top row first.
/// Patches and composed wall textures can have holes; flats never do.
/// </summary>
public sealed class Picture
{
	/// <param name="width">Width in pixels.</param>
	/// <param name="height">Height in pixels.</param>
	/// <param name="pixels">Palette indices, row by row.</param>
	/// <param name="opaque">Which pixels are drawn. Null means every pixel is.</param>
	public Picture( int width, int height, byte[] pixels, bool[] opaque = null )
	{
		if ( pixels.Length != width * height )
			throw new ArgumentException( "Pixel count does not match width * height.", nameof( pixels ) );
		if ( opaque is not null && opaque.Length != pixels.Length )
			throw new ArgumentException( "Opacity mask does not match the pixels.", nameof( opaque ) );

		Width = width;
		Height = height;
		Pixels = pixels;
		Opaque = opaque;
	}

	public int Width { get; }

	public int Height { get; }

	/// <summary>Palette indices. The value under a transparent pixel is meaningless.</summary>
	public byte[] Pixels { get; }

	/// <summary>True where a pixel is drawn. Null if the picture has no holes.</summary>
	public bool[] Opaque { get; }

	public bool IsOpaque( int x, int y ) => Opaque is null || Opaque[y * Width + x];

	/// <summary>Writes RGBA bytes, four per pixel, row by row. Transparent pixels are all zero.</summary>
	public void WriteRgba( Palette palette, Span<byte> destination )
	{
		for ( int i = 0; i < Pixels.Length; i++ )
		{
			var o = i * 4;
			if ( Opaque is not null && !Opaque[i] )
			{
				destination[o] = destination[o + 1] = destination[o + 2] = destination[o + 3] = 0;
				continue;
			}

			var colour = palette.Colors[Pixels[i]];
			destination[o] = colour.r;
			destination[o + 1] = colour.g;
			destination[o + 2] = colour.b;
			destination[o + 3] = 255;
		}
	}
}
