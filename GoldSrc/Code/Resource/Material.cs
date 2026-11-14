using Sandbox.Mounting;

namespace GoldSrc;

public class MaterialLoader( string texturePath, string textureName = "" ) : ResourceLoader<GameMount>
{
	private readonly string TexturePath = texturePath;

	protected override object Load()
	{
		var material = Material.Create( Path, "shaders/goldsrc.shader" );
		material?.Set( "Color", Texture.Load( TexturePath ) );
		if ( material is not null && Host is not null )
			Host.BindSurface( material, textureName );

		return material;
	}
}
