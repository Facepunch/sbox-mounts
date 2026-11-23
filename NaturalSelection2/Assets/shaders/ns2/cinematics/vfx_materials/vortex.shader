//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/vortex.surface_shader
//
//     const float3 tint = float3(0.4, 0.4, 1);
//
//     float3 bordermask = tex2D( MaskMap, input.texCoord ).rgb;
//     float3 bubbles  = tex2D( EmissiveMap, input.texCoord * float2(2, 1.65) + float2( time * .58, time * .43)).rgb;
//     float3 bubbles2 = tex2D( EmissiveMap, input.texCoord * float2(1, 1.23) + float2( time * .35, time * .53)).rgb;
//     float3 bubbles3 = tex2D( EmissiveMap, input.texCoord * float2(1, 2.23) + float2( time * .17, time * .43)).rgb;
//     float fresnel = pow(max(-input.vsNormal.z, 0), 2);
//
//     material.emissive = ((bubbles2 + bubbles) * tint * bordermask) - (bubbles3 * bubbles);
//     material.opacity  = ((bubbles2 + bubbles) * 5) * bordermask * fresnel;
//
// The vortex bubble an alien is trapped in. Three samples of one texture at
// different scales and speeds, but they are not combined the usual way:
//
//  - bubbles and bubbles2 are ADDED and tinted blue - that is the visible film.
//  - bubbles3 * bubbles is then SUBTRACTED. A negative emissive term is unusual
//    and it is what carves dark cells into the film, so it reads as a churning
//    membrane rather than a uniform glow. The result can go below zero, and
//    that is deliberate - it eats into whatever the additive pass is over.
//
// fresnel is pow(N dot V, 2) un-inverted, so opacity is highest facing the
// camera. Combined with opacity being an unclamped additive gain in the
// WriteEmissive pass, the bubble is brightest through its centre and falls off
// at the silhouette - the opposite of a rim-lit shell.
//
// Only emissive and opacity are written, so there is no G-buffer pass, but
// alphaTest is on, so the surface still clips where opacity is low.
//=============================================================================

HEADER
{
	Description = "NS2 Vortex";
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

	CreateInputTexture2D( EmissiveMap, Srgb, 8, "None", "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( MaskMap,     Srgb, 8, "None", "_mask",     ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tMaskMap     < Channel( RGBA, Box( MaskMap ),     Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		const float3 tint = float3( 0.4, 0.4, 1.0 );

		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		float3 bordermask = Tex2DS( g_tMaskMap, g_sWrap, uv ).rgb;
		float3 bubbles  = Tex2DS( g_tEmissiveMap, g_sWrap, uv * float2( 2.0, 1.65 ) + float2( t * 0.58, t * 0.43 ) ).rgb;
		float3 bubbles2 = Tex2DS( g_tEmissiveMap, g_sWrap, uv * float2( 1.0, 1.23 ) + float2( t * 0.35, t * 0.53 ) ).rgb;
		float3 bubbles3 = Tex2DS( g_tEmissiveMap, g_sWrap, uv * float2( 1.0, 2.23 ) + float2( t * 0.17, t * 0.43 ) ).rgb;

		// max(-vsNormal.z, 0) in Spark's view space is N dot V, un-inverted here.
		float nDotV = saturate( dot( normalize( i.vNormalWs ), normalize( -i.vPositionWithOffsetWs.xyz ) ) );
		float fresnel = pow( nDotV, 2.0 );

		// The subtraction is deliberate and can go negative - see the header.
		float3 emissive = ( ( bubbles2 + bubbles ) * tint * bordermask ) - ( bubbles3 * bubbles );
		float3 opacity = ( ( bubbles2 + bubbles ) * 5.0 ) * bordermask * fresnel;

		// alphaTest in the original.
		clip( opacity.x - 0.5 );

		// Spark truncates the float3 assigned to opacity to .x.
		return float4( emissive, opacity.x );
	}
}
