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

		var diffuse = LoadBitmap( shader.DiffuseBitmapTagIndex );
		if ( diffuse is null ) return DefaultMaterial;

		if ( shader.IsEmblem )
		{
			var emblem = Material.Load( "materials/halo2_emblem.vmat" ).CreateCopy();
			emblem.Set( "g_tColor", diffuse );
			return emblem;
		}

		var bump = LoadBitmap( shader.BumpBitmapTagIndex );
		var changeColor = LoadBitmap( shader.ChangeColorBitmapTagIndex );
		var template = changeColor is null ? "materials/halo2_model.vmat" : "materials/halo2_model_cc.vmat";
		var material = Material.Load( template ).CreateCopy();
		material.Set( "g_tBaseMap", diffuse );
		if ( bump is not null ) material.Set( "g_tNormalMap", bump );
		if ( changeColor is not null ) material.Set( "g_tChangeColorMap", changeColor );
		if ( LoadBitmap( shader.EnvironmentBitmapTagIndex ) is { } environment )
		{
			material.Set( "g_tEnvironment", environment );
			material.Set( "g_vEnvironmentFacing", shader.EnvironmentFacing );
			material.Set( "g_vEnvironmentGlancing", shader.EnvironmentGlancing );
		}
		if ( LoadBitmap( shader.DetailBitmapTagIndex ) is { } detail )
		{
			material.Set( "g_tDetail", detail );
			material.Set( "g_vDetailTransform", shader.DetailTransform );
		}
		return material;
	}

	Texture LoadBitmap( int index )
	{
		var bitmap = mapFile.GetTag( index );
		if ( bitmap?.PrimaryClass != "mtib" ) return null;
		return Texture.Load( $"mount://halo2/{mapName}/{bitmap.TagPath.Replace( '\\', '/' )}.vtex" );
	}
}
