using System;
using Sandbox.Mounting;
using Halo2.Blam;

namespace Halo2;

/// <summary>
/// Loads a Halo 2 shader (shad) tag as a material.
/// Uses the halo2_model shader with separate change_color_map for tinting.
/// </summary>
public class Halo2MaterialLoader( MapFile map, TagEntry tag ) : ResourceLoader<Halo2Mount>
{
	static readonly Material DefaultMaterial = Material.Load( "materials/dev/primary_white.vmat" );

	ShaderTag parsed;
	ShaderTag Shader => parsed ??= new ShaderTag( map, tag );

	internal string PathForObject( TagEntry biped )
	{
		if ( Shader.ChangeColorBitmapTagIndex < 0 && Shader.Effect is not (ShaderTag.EffectPass.Emblem or ShaderTag.EffectPass.Hair) )
			return Path;
		return map.ReadInt32( map.VaToOffset( (int)biped.DataVirtualAddress ) + 0xac ) > 0
			? $"{Path}/{biped.Datum & 0xffff}.vmat" : Path;
	}

	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = ResourceType.Material;
		if ( !name.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase ) ||
			!int.TryParse( name.AsSpan( 0, name.Length - 5 ), out int index ) ||
			map.GetTag( index ) is not { PrimaryClass: "dpib", DataSize: >= 0xb4 } biped )
			return null;
		return new ObjectMaterialLoader( biped );
	}

	sealed class ObjectMaterialLoader( TagEntry biped ) : ResourceLoader<Halo2Mount>
	{
		protected override object Load()
		{
			var source = (Halo2MaterialLoader)Parent;
			var material = source.Create( Path );
			if ( material != DefaultMaterial )
				source.ApplyColors( material, biped );
			return material;
		}
	}

	protected override object Load() => Create( Path );

	Material Create( string name )
	{
		var shader = Shader;

		Texture LoadBitmap( int index )
		{
			var bitmap = map.GetTag( index );
			if ( bitmap?.PrimaryClass != "mtib" ) return null;
			return Texture.Load( Host.AssetPath( map, bitmap.TagPath, "vtex" ) );
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
		if ( changeColor is not null ) material.SetFeature( "F_CHANGE_COLOR", shader.ChangeColorChannels == 4 ? 3 : shader.ChangeColorChannels );
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

	static readonly string[] ColorParameters = ["g_vPrimaryColor", "g_vSecondaryColor", "g_vTertiaryColor", "g_vQuaternaryColor"];

	void ApplyColors( Material material, TagEntry biped )
	{
		int block = map.VaToOffset( (int)biped.DataVirtualAddress ) + 0xac;
		int count = Math.Min( map.ReadInt32( block ), ColorParameters.Length );
		int offset = map.VaToOffset( map.ReadInt32( block + 4 ) );
		for ( int i = 0; i < count; i++, offset += 16 )
		{
			if ( map.ReadInt32( offset ) == 0 ) continue;
			using var color = map.OpenReader( map.VaToOffset( map.ReadInt32( offset + 4 ) ) + 4, 12 );
			material.Set( ColorParameters[i], new Vector3( color.ReadSingle(), color.ReadSingle(), color.ReadSingle() ) );
		}
	}

}
