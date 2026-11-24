HEADER
{
	Description = "Diesel map surface";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Transparency" );
	Feature( F_TRANSLUCENT, 0..1, "Transparency" );
	Feature( F_EFFECT_BLEND, 0..2( 0="Alpha", 1="Additive", 2="Multiply" ), "Effect blend" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
	#include "common/shared.hlsl"
	#define CUSTOM_MATERIAL_INPUTS
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vColor : COLOR0;
};

VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		return FinalizeVertex( i );
	}
}

PS
{
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_EFFECT_BLEND, F_EFFECT_BLEND, Sys( ALL ) );
	#if S_EFFECT_BLEND
		#define BLEND_MODE_ALREADY_SET
	#endif
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#if S_EFFECT_BLEND && !S_MODE_DEPTH
		RenderState( BlendEnable, true );
		RenderState( DepthWriteEnable, false );
		#if S_EFFECT_BLEND == 1
			RenderState( SrcBlend, SRC_ALPHA );
			RenderState( DstBlend, ONE );
		#else
			RenderState( SrcBlend, DEST_COLOR );
			RenderState( DstBlend, ZERO );
		#endif
	#endif
	#if S_RENDER_BACKFACES
		RenderState( CullMode, NONE );
	#else
		RenderState( CullMode, BACK );
	#endif

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Diffuse, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( Opacity, Linear, 8, "None", "_opacity", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tDiffuse < Channel( RGBA, Box( Diffuse ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tOpacity < Channel( RGBA, Box( Opacity ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NormalMap, Linear, 8, "None", "_normal", ",0/,0/0", Default4( 0.5, 0.5, 1, 0.5 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( Emission, Srgb, 8, "None", "_emission", ",0/,0/0", Default4( 0, 0, 0, 1 ) );
	Texture2D g_tEmission < Channel( RGBA, Box( Emission ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	float g_flEmissionStrength < Default1( 0 ); >;
	CreateInputTexture2D( BlendDiffuse, Srgb, 8, "None", "_blend", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( BlendMask, Linear, 8, "None", "_blendmask", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tBlendDiffuse < Channel( RGBA, Box( BlendDiffuse ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tBlendMask < Channel( RGBA, Box( BlendMask ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flGroundBlend < Default1( 0 ); >;
	float g_flBlendMaskInAlpha < Default1( 0 ); >;
	CreateInputTexture2D( Detail, Srgb, 8, "None", "_detail", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tDetail < Channel( RGBA, Box( Detail ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	float g_flDetailBlend < Default1( 0 ); >;
	float g_flEffect < Default1( 0 ); >;
	float g_flViewAngleFalloff < Default1( 0 ); >;
	float g_flFalloffExponent < Default1( 1 ); >;
	float g_flEffectIntensity < Default1( 1 ); >;
	float2 g_vDetailScroll < Default2( 0, 0 ); >;
	float g_flHasNormal < Default1( 0 ); >;
	float g_flNormalRA < Default1( 0 ); >;
	float g_flHasOpacity < Default1( 0 ); >;
	float g_flVertexColor < Default1( 0 ); >;
	float g_flBackdrop < Default1( 0 ); >;
	float g_flRoughness < Default1( 0.8 ); >;
	float g_flBlueMaskTint < Default1( 0 ); >;
	float3 g_vMaskTint < Default3( 1, 1, 1 ); >;
	float2 g_vDiffuseScroll < Default2( 0, 0 ); >;
	float2 g_vNormalScroll < Default2( 0, 0 ); >;
	float2 g_vEmissionScroll < Default2( 0, 0 ); >;
	float g_flAlphaReference < Default1( 0.5 ); >;
	float3 g_vTint < Default3( 1, 1, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		// Wrap just the time offset: fract(UV) would break sampling derivatives at tile edges.
		float2 diffuseUv = i.vTextureCoords.xy + frac( g_flTime * g_vDiffuseScroll );
		float2 normalUv = i.vTextureCoords.xy + frac( g_flTime * g_vNormalScroll );
		float2 emissionUv = i.vTextureCoords.xy + frac( g_flTime * g_vEmissionScroll );
		float4 diffuse = Tex2DS( g_tDiffuse, g_sWrap, diffuseUv );
		if ( g_flGroundBlend > 0.5 )
		{
			float3 baseLayer = Tex2DS( g_tBlendDiffuse, g_sWrap, i.vTextureCoords.xy ).rgb;
			float mask = g_flBlendMaskInAlpha > 0.5 ? diffuse.a : Tex2DS( g_tBlendMask, g_sWrap, i.vTextureCoords.xy ).r;
			float painted = saturate( i.vColor.a );
			// Preview height-mask transition. Preserve fully painted endpoints exactly.
			float blend = painted <= 0 ? 0 : painted >= 1 ? 1 : smoothstep( mask - 0.1, mask + 0.1, painted );
			diffuse.rgb = lerp( baseLayer, diffuse.rgb, blend );
		}
		if ( g_flBlueMaskTint > 0.5 )
		{
			// Replace blue-only pigment while retaining neutral wear and surface detail.
			float mask = max( diffuse.b - max( diffuse.r, diffuse.g ), 0 );
			diffuse.rgb = diffuse.rgb - float3( 0, 0, mask ) + mask * g_vMaskTint;
		}
		float opacity = g_flHasOpacity > 0.5 ? Tex2DS( g_tOpacity, g_sWrap, i.vTextureCoords.xy ).r : diffuse.a;
		#if S_ALPHA_TEST
			clip( opacity - g_flAlphaReference );
		#endif

		if ( g_flEffect > 0.5 )
		{
			float3 color = diffuse.rgb * g_vTint;
			if ( g_flDetailBlend > 0.5 )
			{
				float3 detail = Tex2DS( g_tDetail, g_sWrap, i.vTextureCoords.xy + frac( g_flTime * g_vDetailScroll ) ).rgb;
				color = g_flDetailBlend < 1.5 ? color + detail : color * detail;
			}
			if ( g_flVertexColor > 0.5 )
			{
				color *= i.vColor.rgb;
				opacity *= i.vColor.a;
			}
			color *= g_flEffectIntensity;
			if ( g_flViewAngleFalloff > 0.5 && g_flFalloffExponent > 0 )
			{
				float3 view = normalize( g_vCameraPositionWs - ( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz ) );
				// Two-sided glow cards fade symmetrically; exponent zero means no fade.
				float facing = saturate( abs( dot( normalize( i.vNormalWs ), view ) ) );
				opacity *= pow( facing, g_flFalloffExponent );
			}
			#if S_EFFECT_BLEND == 2
				// White is neutral for framebuffer multiplication.
				return float4( lerp( float3( 1, 1, 1 ), color, saturate( opacity ) ), 1 );
			#else
				return float4( color, opacity );
			#endif
		}

		Material m = Material::Init();
		m.Albedo = diffuse.rgb * g_vTint;
		if ( g_flVertexColor > 0.5 ) m.Albedo *= i.vColor.rgb;
		// Self-illumination maps modulate the surface colour (white masks also occur
		// on coloured cables and lamps), rather than replacing it with white light.
		m.Emission = g_flEmissionStrength > 0 ? m.Albedo * Tex2DS( g_tEmission, g_sWrap, emissionUv ).rgb * g_flEmissionStrength : float3( 0, 0, 0 );
		// Diesel backdrop surfaces carry their own lighting. Shading the enormous sky
		// dome as an ordinary surface introduces self-shadow bands and triangle seams.
		if ( g_flBackdrop > 0.5 )
			return float4( m.Albedo + m.Emission, S_TRANSLUCENT ? opacity : 1 );
		m.Normal = normalize( i.vNormalWs );
		if ( g_flHasNormal > 0.5 )
		{
			float4 packed = Tex2DS( g_tNormalMap, g_sWrap, normalUv );
			float3 normalTs = packed.rgb * 2 - 1;
			if ( g_flNormalRA > 0.5 )
			{
				normalTs.xy = packed.ra * 2 - 1;
				normalTs.z = sqrt( saturate( 1 - dot( normalTs.xy, normalTs.xy ) ) );
			}
			m.Normal = TransformNormal( normalize( normalTs ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		}
		#if S_RENDER_BACKFACES
			m.Normal *= i.face ? 1 : -1;
		#endif
		m.Roughness = g_flRoughness;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = S_TRANSLUCENT ? opacity : 1;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = i.vTextureCoords.xy;
		return ShadingModelStandard::Shade( i, m );
	}
}
