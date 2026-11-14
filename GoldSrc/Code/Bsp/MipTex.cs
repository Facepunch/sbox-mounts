using System;
using System.IO;
using System.Text;

namespace GoldSrc.Bsp;

/// <summary>
/// A decoded GoldSrc miptex: 8bpp indices plus the 256 colour palette that goes with them.
/// Textures that live in an external WAD arrive here with no pixel data until they get resolved.
/// </summary>
internal sealed class BspTexture
{
	public string Name { get; init; } = "";
	public int Width { get; init; }
	public int Height { get; init; }

	/// <summary>
	/// One palette index per pixel, or null when the pixels live in an external WAD.
	/// </summary>
	public byte[] Indices { get; set; }

	/// <summary>
	/// 256 RGB triples, or null when the pixels live in an external WAD.
	/// </summary>
	public byte[] Palette { get; set; }

	public bool HasPixels => Indices is not null && Palette is not null;

	/// <summary>
	/// Animated ("+0name") and random-tiling ("-0name") textures carry a two character prefix in
	/// front of the name that actually decides how the surface is drawn.
	/// </summary>
	private ReadOnlySpan<char> BaseName => Name.AsSpan( Name.Length > 2 && Name[0] is '+' or '-' ? 2 : 0 );

	/// <summary>
	/// Sky brushes are drawn by the skybox rather than as world geometry.
	/// </summary>
	public bool IsSky => BaseName.StartsWith( "sky", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// Liquids are named "!water", "!lava" etc, though a handful of maps drop the prefix.
	/// </summary>
	public bool IsLiquid => BaseName is ['!', ..]
		|| BaseName.StartsWith( "water", StringComparison.OrdinalIgnoreCase )
		|| BaseName.StartsWith( "laser", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// A leading '{' means palette index 255 is punched out.
	/// </summary>
	public bool IsMasked => BaseName is ['{', ..];

	/// <summary>
	/// A leading '~' marks a light emitting texture, which we render unlit at full brightness.
	/// </summary>
	public bool IsEmissive => BaseName is ['~', ..];

	private static readonly HashSet<string> ToolTextures = new( StringComparer.OrdinalIgnoreCase )
	{
		"aaatrigger", "clip", "clipbevel", "clipbevelbrush", "cliphull1", "cliphull2", "cliphull3",
		"contentwater", "hint", "null", "origin", "skip", "solidhint", "bevel", "boundingbox",
	};

	/// <summary>
	/// Editor-only brush faces (triggers, clip brushes, hints) which should never be drawn.
	/// </summary>
	public bool IsTool => ToolTextures.Contains( Name );

	public static BspTexture Missing( int index ) => new()
	{
		Name = $"__missing_{index}",
		Width = 1,
		Height = 1,
		Indices = new byte[1],
		Palette = new byte[768],
	};
}

/// <summary>
/// Reader for the miptex_t block shared by BSP texture lumps and WAD3 lumps.
/// </summary>
internal static class MipTex
{
	public const int HeaderSize = 40;

	/// <summary>
	/// Reads a miptex header (and its pixels/palette when they're present) from
	/// <paramref name="baseOffset"/>, which must be the absolute stream position of the header.
	/// </summary>
	public static BspTexture Read( BinaryReader reader, long baseOffset )
	{
		var stream = reader.BaseStream;
		if ( baseOffset + HeaderSize > stream.Length )
			return null;

		stream.Seek( baseOffset, SeekOrigin.Begin );

		var name = ReadName( reader );
		var width = reader.ReadInt32();
		var height = reader.ReadInt32();

		if ( width <= 0 || height <= 0 || width > 4096 || height > 4096 )
			return null;

		var offsets = new uint[4];
		for ( var i = 0; i < 4; ++i )
			offsets[i] = reader.ReadUInt32();

		var texture = new BspTexture { Name = name, Width = width, Height = height };

		// Zero offsets mean the pixels live in an external WAD, we only got the name and size here.
		if ( offsets[0] == 0 )
			return texture;

		var pixelCount = width * height;
		if ( baseOffset + offsets[0] + pixelCount > stream.Length )
			return texture;

		stream.Seek( baseOffset + offsets[0], SeekOrigin.Begin );
		var indices = reader.ReadBytes( pixelCount );

		// The palette sits just past the smallest mip, prefixed with its (always 256) entry count.
		var paletteOffset = baseOffset + offsets[3] + (pixelCount / 64);
		if ( paletteOffset + 2 + 768 > stream.Length )
			return texture;

		stream.Seek( paletteOffset, SeekOrigin.Begin );
		if ( reader.ReadUInt16() != 256 )
			return texture;

		texture.Indices = indices;
		texture.Palette = reader.ReadBytes( 768 );
		return texture;
	}

	public static BspTexture Read( byte[] data )
	{
		if ( data is null || data.Length < HeaderSize )
			return null;

		using var stream = new MemoryStream( data, false );
		using var reader = new BinaryReader( stream );
		return Read( reader, 0 );
	}

	/// <summary>
	/// Expands 8bpp indices into RGBA8888, punching out index 255 for masked textures.
	/// </summary>
	public static byte[] ToRgba( BspTexture texture, bool masked )
	{
		var pixelCount = texture.Width * texture.Height;
		var rgba = new byte[pixelCount * 4];

		var indices = texture.Indices;
		var palette = texture.Palette;

		for ( var i = 0; i < pixelCount; ++i )
		{
			var index = indices[i];
			var dst = i * 4;

			if ( masked && index == 255 )
				continue;

			var src = index * 3;
			rgba[dst + 0] = palette[src + 0];
			rgba[dst + 1] = palette[src + 1];
			rgba[dst + 2] = palette[src + 2];
			rgba[dst + 3] = 255;
		}

		return rgba;
	}

	private static string ReadName( BinaryReader reader )
	{
		var bytes = reader.ReadBytes( 16 );
		var length = Array.IndexOf( bytes, (byte)0 );
		return Encoding.ASCII.GetString( bytes, 0, length < 0 ? bytes.Length : length );
	}
}
