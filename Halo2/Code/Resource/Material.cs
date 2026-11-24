using Sandbox.Mounting;
using Halo2.Blam;

namespace Halo2;

/// <summary>
/// Loads a Halo 2 shader (shad) tag as a material.
/// Uses the halo2_model shader with separate change_color_map for tinting.
/// </summary>
public class Halo2MaterialLoader( MapFile mapFile, TagEntry shaderTag, string mapName ) : ResourceLoader<Halo2Mount>
{
	static readonly Material DefaultMaterial = Material.Load( "materials/dev/primary_white.vmat" );

	protected override object Load()
	{
		var shader = new ShaderTag( mapFile, shaderTag );

		if ( shader.DiffuseBitmapTagIndex < 0 )
			return DefaultMaterial;

		var bitmapTag = mapFile.GetTag( shader.DiffuseBitmapTagIndex );
		if ( bitmapTag == null || bitmapTag.PrimaryClass != "mtib" )
			return DefaultMaterial;

		var texturePath = $"mount://halo2/{mapName}/{bitmapTag.TagPath.Replace( '\\', '/' )}.vtex";
		var texture = Texture.Load( texturePath );

		if ( texture == null )
			return DefaultMaterial;

		// Load bump/normal map if available
		Texture bumpTexture = null;
		if ( shader.BumpBitmapTagIndex >= 0 )
		{
			var bumpTag = mapFile.GetTag( shader.BumpBitmapTagIndex );
			if ( bumpTag != null && bumpTag.PrimaryClass == "mtib" )
			{
				var bumpPath = $"mount://halo2/{mapName}/{bumpTag.TagPath.Replace( '\\', '/' )}.vtex";
				bumpTexture = Texture.Load( bumpPath );
			}
		}

		// Use change color variant if shader has a change_color_map
		if ( shader.ChangeColorBitmapTagIndex >= 0 )
		{
			var ccTag = mapFile.GetTag( shader.ChangeColorBitmapTagIndex );
			if ( ccTag != null && ccTag.PrimaryClass == "mtib" )
			{
				var ccPath = $"mount://halo2/{mapName}/{ccTag.TagPath.Replace( '\\', '/' )}.vtex";
				var ccTexture = Texture.Load( ccPath );
				if ( ccTexture != null )
				{
					var ccMat = Material.Load( "materials/halo2_model_cc.vmat" ).CreateCopy();
					ccMat.Set( "BaseMap", texture );
					ccMat.Set( "ChangeColorMap", ccTexture );
					if ( bumpTexture != null )
						ccMat.Set( "NormalMap", bumpTexture );
					return ccMat;
				}
			}
		}

		var mat = Material.Load( "materials/halo2_model.vmat" ).CreateCopy();
		mat.Set( "BaseMap", texture );
		if ( bumpTexture != null )
			mat.Set( "NormalMap", bumpTexture );
		return mat;
	}
}
