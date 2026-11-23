//=============================================================================
// Port of NS2 ns2/shaders/proximity_forcefield.surface_shader
//
//     float distance = input.vsPosition.z;
//     const float max_dist = 6.0f;
//     const float min_dist = 2.0f;
//     float hide_opacity = saturate((max_dist - distance) / (max_dist - min_dist));
//     hide_opacity = hide_opacity * hide_opacity;
//
//     ... identical five samples to forcefield ...
//
//     material.emissive = ( noise * ash * noise3 * 10 * noise2 + (0.0008 * mask)) * tint * hide_opacity;
//
// The player-blocking forcefield. Line for line the same as
// cinematics/vfx_materials/forcefield, with one addition: a proximity fade.
//
// Note the fade runs the "wrong" way round on purpose. hide_opacity is 1 when
// distance is at or below min_dist and 0 beyond max_dist, so the field is
// brightest when you are stood right against it and vanishes at range. It is
// squared to bias the falloff towards the near end. This is the gameplay tell
// that you are close enough for the field to matter.
//
// Spark measures that distance in metres along the view axis, so both
// thresholds are converted to inches here. Using the view-axis projection
// rather than a radial distance keeps it matching input.vsPosition.z:
// vPositionWithOffsetWs is already camera relative, so projecting it onto the
// camera forward vector gives the same planar depth.
//
// fresnel is computed and never used in the original, exactly as in forcefield.
//
// Writes only emissive, so no WriteDeferred technique: no G-buffer, no depth
// write, no occlusion, composited additively.
//=============================================================================

HEADER
{
	Description = "NS2 Proximity Forcefield";
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

		// input.vsPosition.z - planar depth along the view axis, in inches here.
		float flDistance = dot( i.vPositionWithOffsetWs.xyz, g_vCameraDirWs.xyz );

		const float flMaxDist = 6.0 * NS2_METRE;
		const float flMinDist = 2.0 * NS2_METRE;

		float hideOpacity = saturate( ( flMaxDist - flDistance ) / ( flMaxDist - flMinDist ) );
		hideOpacity = hideOpacity * hideOpacity;

		const float3 tint = float3( 16.0, 38.0, 55.0 );

		float3 mask   = Ns2SrgbToLinear( Tex2DS( g_tMaskMap,    g_sWrap, uv * 6.0 + float2( 0.0, t * 0.2 ) ).rgb );
		float3 ash    = Ns2SrgbToLinear( Tex2DS( g_tGlowpoints, g_sWrap, uv * 2.0 ).rgb );
		float3 noise  = Ns2SrgbToLinear( Tex2DS( g_tNoiseMap,   g_sWrap, uv * 0.02 + float2( t * -0.04, t * -0.1 ) ).rgb );
		float3 noise2 = Ns2SrgbToLinear( Tex2DS( g_tGlowpoints, g_sWrap, uv * float2( 0.02, 0.5 ) + float2( t * -0.01, t * 0.05 ) ).rgb );
		float3 noise3 = Ns2SrgbToLinear( Tex2DS( g_tGlowpoints, g_sWrap, uv * float2( 1.2, 1.1 ) + float2( 0.0, t * 0.02 ) ).rgb );

		float3 emissive = ( noise * ash * noise3 * 10.0 * noise2 + ( 0.0008 * mask ) ) * tint * hideOpacity;

		return float4( emissive, 1.0 );
	}
}
