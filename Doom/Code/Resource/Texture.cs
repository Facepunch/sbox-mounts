using System;
using Doom.Wad;

namespace Doom;

/// <summary>
/// A paletted picture (flat, wall texture or sprite) as an RGBA texture. Surfaces are always
/// texture arrays, one slice per animation frame, because the Doom shader cycles through slices.
/// </summary>
public sealed class PictureTextureLoader( Palette palette, Func<Picture>[] frames, bool array ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var pictures = frames.Select( f => f() ).ToArray();
		if ( pictures.Length == 0 || pictures[0] is null )
			return null;

		int width = pictures[0].Width;
		int height = pictures[0].Height;
		int slice = width * height * 4;
		var data = new byte[slice * pictures.Length];

		for ( int f = 0; f < pictures.Length; f++ )
		{
			// Every frame of an animation has to match the first in size; Doom has the same rule.
			var picture = pictures[f] is { } p && p.Width == width && p.Height == height ? p : pictures[0];
			picture.WriteRgba( palette, data.AsSpan( f * slice, slice ) );
		}

		if ( !array )
		{
			return Texture.Create( width, height, ImageFormat.RGBA8888 )
				.WithName( Path )
				.WithData( data )
				.WithStaticUsage()
				.Finish();
		}

		return Texture.CreateArray( width, height, pictures.Length, ImageFormat.RGBA8888 )
			.WithName( Path )
			.WithData( data )
			.WithStaticUsage()
			.Finish();
	}
}
