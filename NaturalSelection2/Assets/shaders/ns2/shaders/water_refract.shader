//=============================================================================
// Port of NS2 ns2/shaders/water_refract.surface_shader
//
//     float2 offset = float2(time, 0);
//     material.transmissionColor = transmissionColor;
//     float3 tsNormal = scale1 * DecodeNormal( tex2D(normalMap, input.texCoord * 2 + offset * speed1) ) +
//                       scale2 * DecodeNormal( tex2D(normalMap, input.texCoord + offset.yx * speed2) );
//     float2 vsNormal;
//     vsNormal.x = tsNormal.x * input.vsTangent;
//     vsNormal.y = tsNormal.y * input.vsBinormal;
//     material.ssDistortion = vsNormal.xy;
//
// Two samples of the same normal map crossing each other: the first tiled 2x
// and scrolling in U, the second tiled 1x and scrolling in V, which is the
// classic cheap moving-water look. Nothing is lit - the surface only bends and
// tints whatever is behind it.
//=============================================================================

HEADER
{
	Description = "NS2 Water Refract";
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
	// is what puts the surface in the translucent pass. Without it the draw
	// happens in the opaque pass, where the framebuffer copy and depth chain have
	// not been resolved yet, and sampling them just smears the colour buffer.
	#define DEPTH_STATE_ALREADY_SET 1
	#define BLEND_MODE_ALREADY_SET 1
	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 1
	#endif
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"
	#include "ns2/ns2_refract.hlsl"

	BoolAttribute( bWantsFBCopyTexture, true );

	// A Spark refractor contributes no colour and no depth of its own, it only
	// warps what is already there, so this replaces its pixels outright.
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ZERO );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( NormalMap, Linear, 8, "NormalizeNormals", "_normal", ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float3 g_vTransmissionColor < UiGroup( ",0/,0/0" ); Default3( 1.0, 1.0, 1.0 ); >;
	float g_flScale1 < UiGroup( ",0/,0/0" ); Default1( 0.01 ); Range1( 0, 1 ); >;
	float g_flScale2 < UiGroup( ",0/,0/0" ); Default1( 0.01 ); Range1( 0, 1 ); >;
	float g_flSpeed1 < UiGroup( ",0/,0/0" ); Default1( 0.1 ); Range1( -10, 10 ); >;
	float g_flSpeed2 < UiGroup( ",0/,0/0" ); Default1( 0.1 ); Range1( -10, 10 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float2 offset = float2( g_flTime, 0.0 );

		float3 tsNormal =
			g_flScale1 * Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv * 2.0 + offset * g_flSpeed1 ) ) +
			g_flScale2 * Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv + offset.yx * g_flSpeed2 ) );

		float2 distortion = Ns2DistortionToScreen( tsNormal, i.vTangentUWs.xyz, i.vTangentVWs.xyz );

		return float4( Ns2Refract( i.vPositionSs.xy, i.vPositionSs.z, distortion, g_vTransmissionColor, 1.0 ), 1.0 );
	}
}
