namespace Renderware;

/// <summary>
/// A texture from a texture dictionary, registered at "textures/txd/texture". Also the mount's record of where the
/// texture lives and what its alpha is like, see <see cref="GTAMount.GetTexture"/>.
/// </summary>
sealed class TextureLoader( TxdFile txd, TextureEntry entry ) : ResourceLoader<GTAMount>
{
	public TxdFile Txd => txd;
	public TextureEntry Entry => entry;

	bool? softAlpha, cutout;

	/// <summary>A soft fade worth blending (beams, glows) rather than on / off (foliage), see <see cref="TxdFile.HasSoftAlpha"/>.</summary>
	public bool HasSoftAlpha => softAlpha ??= txd.HasSoftAlpha( entry );

	/// <summary>Needs alpha testing, see <see cref="TxdFile.HasCutoutAlpha"/>.</summary>
	public bool IsCutout => cutout ??= txd.HasCutoutAlpha( entry );

	protected override object Load()
	{
		var mips = txd.ReadMips( entry );
		if ( mips.Length == 0 )
			return null;

		// upload the dictionary's own mip chain, smallest first - the engine can't generate mips for DXT
		var data = mips.Reverse().SelectMany( m => m ).ToArray();

		return Texture.Create( entry.Width, entry.Height, TxdFile.GetFormat( entry ) )
			.WithName( entry.Name )
			.WithData( data )
			.WithMips( mips.Length )
			.WithStaticUsage()
			.Finish();
	}
}

/// <summary>
/// The material for a texture: our Renderware shader, alpha tested if the texture has alpha.
/// </summary>
sealed class MaterialLoader( TextureLoader texture ) : ResourceLoader<GTAMount>
{
	protected override object Load()
	{
		var material = Material.Create( Path, "shaders/renderware.shader" );
		material.Set( "Color", Texture.Load( texture.Path, false ) );

		// cutouts (fences, foliage, signs); everything is double sided already, see the shader
		if ( texture.Entry.HasAlpha )
			material.SetFeature( "F_ALPHA_TEST", 1 );

		return material;
	}
}
