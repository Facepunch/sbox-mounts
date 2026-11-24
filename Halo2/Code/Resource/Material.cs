using Sandbox.Mounting;
using Halo2.Blam;

namespace Halo2;

/// <summary>
/// Loads a Halo 2 shader (shad) tag as a material.
/// Uses the halo2_model shader with separate change_color_map for tinting.
/// </summary>
public class Halo2MaterialLoader( MapFile mapFile, TagEntry shaderTag ) : ResourceLoader<Halo2Mount>
{
	static readonly Material DefaultMaterial = Material.Load( "materials/dev/primary_white.vmat" );

	protected override object Load() => Create( Host, mapFile, shaderTag, Path );

	internal static Material Create( Halo2Mount mount, MapFile map, TagEntry tag, string name )
	{
		var shader = new ShaderTag( map, tag );

		Texture LoadBitmap( int index )
		{
			var bitmap = map.GetTag( index );
			if ( bitmap?.PrimaryClass != "mtib" ) return null;
			return Texture.Load( mount.AssetPath( map, bitmap.TagPath, "vtex" ) );
		}

		if ( shader.Effect != ShaderTag.EffectPass.None )
		{
			var effect = Material.Create( name, "shaders/halo2_effect.shader" );
			effect.SetFeature( "F_PASS", (int)shader.Effect );
			if ( shader.Effect == ShaderTag.EffectPass.Plasma ) effect.Set( "g_vPlasmaScroll", shader.PlasmaScroll );
			for ( int i = 0; i < shader.EffectMaps.Length; i++ )
			{
				if ( LoadBitmap( shader.EffectMaps[i] ) is { } texture )
					effect.Set( shader.Effect == ShaderTag.EffectPass.Eyes && i == 0 ? "g_tReflection" : $"g_tLayer{i}", texture );
			}
			for ( int i = 0; i < shader.EffectConstants.Length; i++ )
				effect.Set( $"g_vConstant{i}", shader.EffectConstants[i] );
			if ( shader.EffectUV is { } rows )
			{
				int start = shader.Effect == ShaderTag.EffectPass.Eyes ? 2 : 0;
				for ( int i = 0; i < rows.Length; i++ ) effect.Set( $"g_vRow{start + i}", rows[i] );
			}
			return effect;
		}

		var diffuse = LoadBitmap( shader.DiffuseBitmapTagIndex );
		if ( diffuse is null ) return DefaultMaterial;
		var bump = LoadBitmap( shader.BumpBitmapTagIndex );
		var changeColor = LoadBitmap( shader.ChangeColorBitmapTagIndex );
		// Template copies share a batch key. Each set of texture and tint bindings needs its own material.
		var material = Material.Create( name, "shaders/halo2_model.shader" );
		if ( shader.AlphaTest ) material.SetFeature( "F_MASKED", 1 );
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
		if ( LoadBitmap( shader.GlowBitmap ) is { } glow )
		{
			material.Set( "g_tGlow", glow );
			material.Set( "g_vGlow", shader.GlowColor );
		}
		if ( LoadBitmap( shader.DetailBitmapTagIndex ) is { } detail )
		{
			material.Set( "g_tDetail", detail );
			material.Set( "g_vDetailTransform", shader.DetailTransform );
		}
		return material;
	}

}
