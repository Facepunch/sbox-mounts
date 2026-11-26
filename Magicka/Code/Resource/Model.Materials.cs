using System;
using Nv3 = System.Numerics.Vector3;

namespace Magicka;

internal sealed partial class MagickaModelBuilder
{
	private const string OpaqueShader = "shaders/magicka/magicka_model.shader";
	private const string AdditiveShader = "shaders/magicka/magicka_additive.shader";
	private const string TranslucentShader = "shaders/magicka/magicka_translucent.shader";

	internal Material MaterialFor( object effect, Dictionary<object, Material> cache, ref int index )
	{
		if ( effect is not null && cache.TryGetValue( effect, out var cached ) )
			return cached;

		// The game's effect classes carry their blend mode: additive magic, the alpha pass
		// (technique 1 and materials with an alpha below one), and everything else opaque.
		var isAdditive = effect is AdditiveEffect or SkinnedModelBasicEffect { Technique: 2 };
		var isBlended = !isAdditive && effect is SkinnedModelBasicEffect { Technique: 1 }
			or RenderDeferredEffect { Alpha: < 0.99f }
			or BasicEffect { Alpha: < 0.99f };
		var shader = isAdditive ? AdditiveShader : isBlended ? TranslucentShader : OpaqueShader;

		var material = Material.Create( $"{path}/material{index++}.vmat", shader, false );
		if ( material is null )
			return null;

		var tint = Color.White;
		var emission = 0f;
		var specAmount = 0f;
		var specPower = 32f;
		Texture diffuse = null;
		Texture detailTex = null;
		var detailAlpha = 1f;
		var dualLayer = false;
		var layer0AlphaMask = false;
		var layer1AlphaMask = false;
		var sharpness = 1f;
		Texture materialMap = null;

		switch ( effect )
		{
			case RenderDeferredEffect deferred:
				tint = ToColor( deferred.Layer0.DiffuseColor, deferred.Alpha );
				emission = deferred.Layer0.EmissiveAmount;
				specAmount = deferred.Layer0.SpecAmount;
				specPower = deferred.Layer0.SpecPower;
				diffuse = LoadTexture( deferred.Layer0.DiffuseTexture );
				materialMap = LoadTexture( deferred.Layer0.MaterialTexture );
				if ( deferred.Layer1?.DiffuseTexture is not null )
				{
					detailTex = LoadTexture( deferred.Layer1.DiffuseTexture );
					dualLayer = detailTex is not null;
					layer0AlphaMask = deferred.Layer0.AlphaMaskEnabled;
					layer1AlphaMask = deferred.Layer1.AlphaMaskEnabled;
					sharpness = MathF.Max( deferred.Sharpness, 1f );
				}
				break;

			case SkinnedModelBasicEffect basic:
				tint = Color.White;
				emission = basic.EmissiveAmount;
				specAmount = basic.SpecularAmount;
				specPower = basic.SpecularPower;
				materialMap = LoadTexture( basic.SpecularMap, basic.SpecularMapEnabled );

				// Magicka's scene rim light defaults for characters.
				material.Set( "g_flRimGlow", 0.8f );
				material.Set( "g_flRimPower", 1.5f );
				material.Set( "g_flRimBias", 0.2f );

				// Magicka tints only the base map (DiffuseMap1); detail (DiffuseMap0) overlays untinted in the shader.
				// When both slots point to the same texture (e.g. elementals), keep it on Color so the whole mesh tints.
				var sameMap = basic.DiffuseMap0Enabled && basic.DiffuseMap1Enabled
					&& basic.DiffuseMap0.External?.Path is { } p0
					&& string.Equals( p0, basic.DiffuseMap1.External?.Path, StringComparison.OrdinalIgnoreCase );

				if ( !sameMap && basic.DiffuseMap0Enabled && basic.DiffuseMap1Enabled
					&& !basic.DiffuseMap0.IsNull && !basic.DiffuseMap1.IsNull )
				{
					diffuse = LoadTexture( basic.DiffuseMap1, true );
					detailTex = LoadTexture( basic.DiffuseMap0, true );
					detailAlpha = Math.Clamp( basic.Diffuse1Alpha, 0f, 1f );
				}
				else
				{
					diffuse = LoadTexture( basic.DiffuseMap0, basic.DiffuseMap0Enabled )
						?? LoadTexture( basic.DiffuseMap1, basic.DiffuseMap1Enabled );
				}
				break;

			case SkinnedModelDeferredNormalMappedEffect normalMapped:
				tint = ToColor( normalMapped.DiffuseColor, 1 );
				emission = normalMapped.EmissiveAmount;
				specAmount = normalMapped.SpecularAmount;
				specPower = normalMapped.SpecularPower;
				diffuse = LoadTexture( normalMapped.DiffuseMap );
				materialMap = LoadTexture( normalMapped.MaterialMap );
				break;

			case AdditiveEffect additive:
				tint = ToColor( additive.ColorTint, 1 );
				emission = 1;
				diffuse = additive.TextureEnabled ? LoadTexture( additive.Texture ) : null;
				break;

			case BasicEffect basicEffect:
				tint = ToColor( basicEffect.DiffuseColor, basicEffect.Alpha );
				diffuse = LoadTexture( basicEffect.Texture );
				break;
		}

		if ( diffuse is not null )
			material.Set( "Color", diffuse );

		if ( detailTex is not null )
		{
			material.Set( "Detail", detailTex );
			material.Set( "g_flDetailAlpha", detailAlpha );
		}

		if ( dualLayer )
		{
			material.Set( "g_flDualLayer", 1f );
			material.Set( "g_flLayer0AlphaMask", layer0AlphaMask ? 1f : 0f );
			material.Set( "g_flLayer1AlphaMask", layer1AlphaMask ? 1f : 0f );
			material.Set( "g_flSharpness", sharpness );
		}

		if ( materialMap is not null )
		{
			material.Set( "Material", materialMap );
			material.Set( "g_flHasMaterialMap", 1f );
		}

		var opacity = effect switch
		{
			RenderDeferredEffect deferred => deferred.Alpha,
			BasicEffect basicEffect => basicEffect.Alpha,
			_ => 1f,
		};
		material.Set( "g_flOpacity", Math.Clamp( opacity, 0f, 1f ) );

		material.Set( "g_vTint", new Vector3( tint.r, tint.g, tint.b ) );
		material.Set( "g_flEmission", emission );
		material.Set( "g_flSpecAmount", specAmount );
		material.Set( "g_flSpecPower", specPower );

		// Magicka's opaque passes alpha-test the diffuse texture: chains, foliage and effect
		// cutouts are a texture alpha, not a blend mode. Deferred layers that disable alpha or
		// use AlphaMaskEnabled for dual-layer terrain height blending are not alpha cutouts.
		var alphaTested = effect is SkinnedModelBasicEffect { Technique: 0 or 1 or 5 }
			|| effect is RenderDeferredEffect deferredAlpha
				&& !deferredAlpha.Layer0.DiffuseTextureAlphaDisabled
				&& !deferredAlpha.Layer0.AlphaMaskEnabled
			|| effect is BasicEffect { Alpha: < 0.99f };
		material.Set( "g_flAlphaTest", alphaTested ? 0.5f : 0f );

		if ( effect is not null )
			cache[effect] = material;

		return material;
	}

	private static Color ToColor( Nv3 rgb, float alpha ) =>
		new( rgb.X, rgb.Y, rgb.Z, Math.Clamp( alpha, 0f, 1f ) );

	private Texture LoadTexture( ExternalReference reference )
	{
		if ( reference is null )
			return null;

		var resolved = MagickaMount.ResolveExternalPath( file, reference.Path );
		return resolved is null ? null : Texture.Load( host.Url( resolved, "vtex" ) );
	}

	private Texture LoadTexture( ContentRef<Texture2DContent> reference, bool enabled )
	{
		if ( !enabled || reference.IsNull )
			return null;

		if ( reference.Inline is { } inline )
			return MagickaTextures.Create( $"{path}/inline{inlineTextures++}", inline );

		return LoadTexture( reference.External );
	}
}
