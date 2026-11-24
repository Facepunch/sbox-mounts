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

	protected override object Load() => Create( mapFile, shaderTag, mapName, Path );

	internal static Material Create( MapFile map, TagEntry tag, string mapName, string name )
	{
		var shader = new ShaderTag( map, tag );

		Texture LoadBitmap( int index )
		{
			var bitmap = map.GetTag( index );
			if ( bitmap?.PrimaryClass != "mtib" ) return null;
			return Texture.Load( $"mount://halo2/{mapName}/{bitmap.TagPath.Replace( '\\', '/' )}.vtex" );
		}

		var diffuse = LoadBitmap( shader.DiffuseBitmapTagIndex );
		if ( diffuse is null ) return DefaultMaterial;

		if ( shader.IsEmblem )
		{
			var emblem = Material.Create( name, "shaders/complex.shader" );
			emblem.SetFeature( "F_TRANSLUCENT", 1 );
			emblem.Set( "g_tColor", diffuse );
			return emblem;
		}

		var bump = LoadBitmap( shader.BumpBitmapTagIndex );
		var changeColor = LoadBitmap( shader.ChangeColorBitmapTagIndex );
		// Template copies share a batch key. Each set of texture and tint bindings needs its own material.
		var material = Material.Create( name, "shaders/halo2_model.shader" );
		if ( changeColor is not null ) material.SetFeature( "F_CHANGE_COLOR", 1 );
		material.Set( "g_bFogEnabled", true );
		material.Set( "g_tBaseMap", diffuse );
		if ( bump is not null ) material.Set( "g_tNormalMap", bump );
		if ( changeColor is not null ) material.Set( "g_tChangeColorMap", changeColor );
		if ( LoadBitmap( shader.EnvironmentBitmapTagIndex ) is { } environment )
		{
			material.Set( "g_tEnvironment", environment );
			material.Set( "g_flEnvironmentUnsignedZ", shader.EnvironmentUsesUnsignedNormalZ );
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

}
