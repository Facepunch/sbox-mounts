//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/forcefield.surface_shader
//
//     const float3 tint = float3(16, 38, 55);
//
//     float3 mask   = tex2D( maskMap,    input.texCoord * 6 + float2(time * 0, time * 0.2) ).rgb;
//     float3 ash    = tex2D( glowpoints, input.texCoord * 2 ).rgb;
//     float3 noise  = tex2D( noiseMap,   input.texCoord * .02 + float2( time * -0.04, time * -0.1) ).rgb;
//     float3 noise2 = tex2D( glowpoints, input.texCoord * float2( 0.02, .5) + float2(time * -0.01, time * 0.05) ).rgb;
//     float3 noise3 = tex2D( glowpoints, input.texCoord * float2( 1.2, 1.1) + float2(0, time * .02) ).rgb;
//     float fresnel = pow(max(-input.vsNormal.z, 0), 5);
//
//     material.emissive = ( noise * ash * noise3 * 10 * noise2 + (0.0008 * mask)) * tint;
//
// The marine forcefield / phase gate membrane. Used by
// materials/effects/forcefield_tile, which appears in menu_veil, ns2_caged and
// ns2_metro.
//
// Five samples off three textures, and glowpoints is read three separate times
// at wildly different tiling - 2x for the fine "ash" speckle, an extremely
// stretched (0.02, 0.5) for the slow vertical banding, and (1.2, 1.1) for a
// near-1:1 layer that crawls upward. Multiplying them all together is what
// makes the surface read as sparse moving cells rather than a flat sheet:
// every layer has to be bright in the same place for anything to show, so the
// result is mostly black with occasional bright knots.
//
// The 10x is needed to claw the brightness back after that four-way multiply,
// and tint (16, 38, 55) is a deliberately over-1 HDR blue that blooms.
//
// The mask term is added at 0.0008 - essentially a very faint always-there
// wash so the field is not completely invisible where the noise layers cancel.
//
// fresnel is computed in the original and then never used, which is also why
// the shader still asks for the vsNormal param. Ported as authored: the dead
// term is left out rather than "fixed" into the result.
//
// Writes only emissive, so Spark builds no WriteDeferred technique - no
// G-buffer, no depth write, no occlusion - and composites with WriteEmissive's
// src_alpha/one, i.e. additive.
//=============================================================================

HEADER
{
	Description = "NS2 Forcefield";
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
	// Depth and blend state are set explicitly below. sbox_pixel.fxc installs its
	// own translucent depth/blend state unless told not to, and a duplicate
	// RenderState does not override the earlier one - it compiles to a different
	// state entirely - so the guards are required, not just tidiness.
	//
	// S_TRANSLUCENT is also what emits BoolAttribute( translucent, true ), which
	// is what puts the surface in the translucent pass.
	#define DEPTH_STATE_ALREADY_SET 1
	#define BLEND_MODE_ALREADY_SET 1
	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 1
	#endif
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	// A Spark emissive-only material contributes no depth and no albedo, it only
	// adds light, so this never writes depth and blends one/one.
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );
	RenderState( SrcBlendAlpha, SRC_ALPHA );
	RenderState( DstBlendAlpha, ONE );
	RenderState( BlendOpAlpha, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( Glowpoints, Linear, 8, "None", "_glow",  ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NoiseMap,   Linear, 8, "None", "_noise", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( MaskMap,    Linear, 8, "None", "_mask",  ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tGlowpoints < Channel( RGBA, Box( Glowpoints ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNoiseMap   < Channel( RGBA, Box( NoiseMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tMaskMap    < Channel( RGBA, Box( MaskMap ),    Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		const float3 tint = float3( 16.0, 38.0, 55.0 );

		float3 mask   = Ns2SrgbToLinear( Tex2DS( g_tMaskMap,    g_sWrap, uv * 6.0 + float2( 0.0, t * 0.2 ) ).rgb );
		float3 ash    = Ns2SrgbToLinear( Tex2DS( g_tGlowpoints, g_sWrap, uv * 2.0 ).rgb );
		float3 noise  = Ns2SrgbToLinear( Tex2DS( g_tNoiseMap,   g_sWrap, uv * 0.02 + float2( t * -0.04, t * -0.1 ) ).rgb );
		float3 noise2 = Ns2SrgbToLinear( Tex2DS( g_tGlowpoints, g_sWrap, uv * float2( 0.02, 0.5 ) + float2( t * -0.01, t * 0.05 ) ).rgb );
		float3 noise3 = Ns2SrgbToLinear( Tex2DS( g_tGlowpoints, g_sWrap, uv * float2( 1.2, 1.1 ) + float2( 0.0, t * 0.02 ) ).rgb );

		float3 emissive = ( noise * ash * noise3 * 10.0 * noise2 + ( 0.0008 * mask ) ) * tint;

		return float4( emissive, 1.0 );
	}
}
