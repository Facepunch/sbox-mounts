//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/energy_flow.surface_shader
//
//     const float3 tint = float3(1, 3, 20);
//     float4 tex2 = tex2D(emissiveMap, input.texCoord*.3 + float2(time * -.2, time * -.5));
//     float4 tex  = tex2D(emissiveMap, input.texCoord*1.5 + float2(time * .5, time * -0.8)
//                 + DecodeNormal( tex2D(normalMap, input.texCoord*3 + float2(time * -1, time * 2)) ).xy * .1 );
//
//     material.emissive = (tex.rgb + tex2.rgb) * tint;
//     material.opacity  = (tex.rgb + tex2.rgb);
//
// Blue energy streaming along a conduit. Two copies of the same texture scroll
// in opposite directions - one large and slow, one small and fast - and are
// ADDED rather than multiplied, so either layer can show on its own and the
// flow never fully goes dark. The fast layer is also jittered by a third
// scrolling normal map for turbulence.
//
// The tint is (1, 3, 20), very heavily blue-weighted, which is what turns a
// greyscale texture into the cold energy colour.
//
// cloudMap is sampled into tex3 and then never used. Ported as authored - the
// dead sample is dropped rather than invented into the result.
//
// material.opacity is assigned a float3; Spark truncates that to .x. Since only
// emissive and opacity are written this is the WriteEmissive technique alone,
// where opacity is an unclamped additive gain, so it must not be saturated -
// the sum of two textures routinely exceeds 1 and is meant to.
//=============================================================================

HEADER
{
	Description = "NS2 Energy Flow";
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

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		const float3 tint = float3( 1.0, 3.0, 20.0 );

		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		float2 jitter = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv * 3.0 + float2( t * -1.0, t * 2.0 ) ) ).xy * 0.1;

		float3 tex2 = Tex2DS( g_tEmissiveMap, g_sWrap, uv * 0.3 + float2( t * -0.2, t * -0.5 ) ).rgb;
		float3 tex  = Tex2DS( g_tEmissiveMap, g_sWrap, uv * 1.5 + float2( t * 0.5, t * -0.8 ) + jitter ).rgb;

		float3 sum = tex + tex2;

		// Spark truncates the float3 assigned to opacity to .x.
		return float4( sum * tint, sum.x );
	}
}
