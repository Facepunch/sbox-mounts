//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/fusion_tanith.surface_shader
//
//     float3 DecodeNormal(const float4 n) { return (n * 3 - 1).xyz; }   // NOTE: *3, not *2
//
//     const float3 tint = float3(5, 3, 10);
//     float4 tex2 = tex2D(emissiveMap, input.texCoord * 0.3 + float2(time * -0.02, time * -0.05)
//                 + DecodeNormal( tex2D(normalMap, input.texCoord * 3 + float2(time * 0.25, time * 1)) ).xy * .1 );
//     float4 tex  = tex2D(emissiveMap, input.texCoord * 1.5 + float2(time * 0.05, time * -0.08)
//                 + DecodeNormal( tex2D(normalMap, input.texCoord * 3 + float2(time * -0.5, time * 1)) ).xy * .1 );
//
//     material.emissive = (tex.rgb + tex2.rgb) * tint;
//     material.opacity  = (tex.rgb + tex2.rgb) * 0.05;
//
// The fusion reactor glow in ns2_tanith. Structurally this is energy_flow with
// a purple tint and much slower scroll speeds - a churning core rather than a
// current flowing along a pipe - but two details make it genuinely different
// and neither can be shared with the sibling shader:
//
//  1. DecodeNormal is `texNormal * 3 - 1`, not the usual `* 2 - 1`. That maps
//     0..1 to -1..2 instead of -1..1, so the jitter is 1.5x stronger and biased
//     in the positive direction: the distortion pushes the layers one way. This
//     is almost certainly a typo in the original, but it is visible, so
//     Ns2DecodeNormal is deliberately NOT used here.
//  2. Each emissive layer gets its own normal sample scrolling a different way,
//     where energy_flow shares one.
//
// opacity is scaled by 0.05, far dimmer than energy_flow's raw sum - this is a
// large background surface, so it stays subtle.
//
// cloudMap is sampled into tex3 and never used, as in energy_flow. Dropped.
//=============================================================================

HEADER
{
	Description = "NS2 Fusion Tanith";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	#ifndef S_ALPHA_TEST
	#define S_ALPHA_TEST 0
	#endif

	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 1
	#endif

	#include "common/shared.hlsl"

	#define CUSTOM_MATERIAL_INPUTS
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		return FinalizeVertex( ProcessVertex( v ) );
	}
}

PS
{
	#define DEPTH_STATE_ALREADY_SET 1
	#define BLEND_MODE_ALREADY_SET 1
	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 1
	#endif
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, NONE );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );
	RenderState( SrcBlendAlpha, SRC_ALPHA );
	RenderState( DstBlendAlpha, ONE );
	RenderState( BlendOpAlpha, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( EmissiveMap, Srgb,   8, "None", "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "None", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	// Local decode: this shader uses *3-1, unlike every other NS2 shader.
	float3 FusionDecodeNormal( float4 texNormal )
	{
		return texNormal.xyz * 3.0 - 1.0;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		const float3 tint = float3( 5.0, 3.0, 10.0 );

		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		float2 jitter2 = FusionDecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv * 3.0 + float2( t * 0.25, t * 1.0 ) ) ).xy * 0.1;
		float2 jitter  = FusionDecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv * 3.0 + float2( t * -0.5, t * 1.0 ) ) ).xy * 0.1;

		float3 tex2 = Tex2DS( g_tEmissiveMap, g_sWrap, uv * 0.3 + float2( t * -0.02, t * -0.05 ) + jitter2 ).rgb;
		float3 tex  = Tex2DS( g_tEmissiveMap, g_sWrap, uv * 1.5 + float2( t * 0.05, t * -0.08 ) + jitter ).rgb;

		float3 sum = tex + tex2;

		// Spark truncates the float3 assigned to opacity to .x.
		return float4( sum * tint, sum.x * 0.05 );
	}
}
