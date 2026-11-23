//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/elec_trails.surface_shader
//
//     const float3 tint = float3(4, 36, 200);
//
//     float3 mask   = tex2D( maskMap, input.texCoord ).rgb;
//     float3 seq    = tex2D( seqMask, input.texCoord * float2( 1, 0.05) + float2(0, time * 22) ).rgb;
//     float3 noise1 = tex2D( noiseMap, input.texCoord * float2( .2, 0.1) + float2(time * 0.2, time * 6) ).rgb;
//     float3 noise2 = tex2D( noiseMap, input.texCoord * float2( .8, .2)  + float2(time * .5, time * 5) ).rgb;
//
//     material.emissive = (noise1 + noise2) * tint * mask * seq;
//
// Electric arcs travelling along a cable. Every scroll here is extremely fast
// in V and slow in U, which is what sells current running along the length
// rather than a texture sliding across it.
//
// seqMask is the interesting one: sampled at 0.05x vertically and scrolled at
// 22 units/sec, so a tall thin strip of the texture sweeps past twenty times a
// second. It gates the whole result, chopping the continuous noise into
// discrete arcs that fire and die instead of a steady glow.
//
// The tint (4, 36, 200) is dominated by blue and heavily amplified, because the
// inputs are a product of 0..1 textures and would otherwise be near black.
//
// f (input.shaderParam.x) and fresnel are both computed and never used in the
// original. Ported as authored: the dead terms are omitted rather than folded
// into the result.
//
// Only emissive is written, so this is the WriteEmissive technique alone: no
// G-buffer, no depth write, purely additive.
//=============================================================================

HEADER
{
	Description = "NS2 Elec Trails";
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
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( NoiseMap, Srgb, 8, "None", "_noise", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( MaskMap,  Srgb, 8, "None", "_mask",  ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( SeqMask,  Srgb, 8, "None", "_seq",   ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tMaskMap  < Channel( RGBA, Box( MaskMap ),  Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tSeqMask  < Channel( RGBA, Box( SeqMask ),  Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		const float3 tint = float3( 4.0, 36.0, 200.0 );

		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		float3 mask   = Tex2DS( g_tMaskMap, g_sWrap, uv ).rgb;
		float3 seq    = Tex2DS( g_tSeqMask, g_sWrap, uv * float2( 1.0, 0.05 ) + float2( 0.0, t * 22.0 ) ).rgb;
		float3 noise1 = Tex2DS( g_tNoiseMap, g_sWrap, uv * float2( 0.2, 0.1 ) + float2( t * 0.2, t * 6.0 ) ).rgb;
		float3 noise2 = Tex2DS( g_tNoiseMap, g_sWrap, uv * float2( 0.8, 0.2 ) + float2( t * 0.5, t * 5.0 ) ).rgb;

		float3 emissive = ( noise1 + noise2 ) * tint * mask * seq;

		return float4( emissive, 1.0 );
	}
}
