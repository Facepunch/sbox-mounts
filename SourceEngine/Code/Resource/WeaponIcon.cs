using System;

namespace Source1;

/// <summary>A weapon script's HUD icon, white on transparent: a scheme font glyph, or a region of a material's texture.</summary>
internal sealed class SourceWeaponIconLoader( SourceHudTexture icon, string fontFile ) : ResourceLoader<BaseMount>
{
	private const int GlyphHeight = 128;

	protected override object Load()
	{
		var (rgba, width, height) = icon.File is null
			? SourceHudGlyph.Render( Host.GetFileBytes( fontFile ), icon.Character[0], GlyphHeight )
			: Region();
		return Texture.Create( width, height ).WithData( rgba ).WithMips().Finish();
	}

	private (byte[] Rgba, int Width, int Height) Region()
	{
		var material = Host.GetSourceMaterial( icon.File );
		var texture = material.Source.Parameters.GetValueOrDefault( "$basetexture" ) ?? icon.File;
		var vtf = Host.GetSourceTexture( texture ).ReadFrame( 0 );
		var pixels = vtf.ReadRgbaPixels( 0, (icon.X, icon.Y, icon.Width, icon.Height) );
		var rgba = new byte[pixels.Length * 4];
		for ( var i = 0; i < pixels.Length; i++ )
		{
			var pixel = pixels[i];
			rgba[i * 4] = 255;
			rgba[i * 4 + 1] = 255;
			rgba[i * 4 + 2] = 255;
			// Additive sprites draw on black, so brightness is coverage as much as alpha is.
			var coverage = pixel.w * Math.Max( pixel.x, Math.Max( pixel.y, pixel.z ) );
			rgba[i * 4 + 3] = (byte)Math.Clamp( MathF.Round( coverage * 255 ), 0, 255 );
		}
		return (rgba, icon.Width, icon.Height);
	}
}
