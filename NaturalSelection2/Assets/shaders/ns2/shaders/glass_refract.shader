//=============================================================================
// Port of NS2 ns2/shaders/glass_refract.surface_shader
//
//     material.transmissionColor = transmissionColor;
//     material.ssDistortion = DecodeNormal( tex2D(normalMap, input.texCoord) ).xy * 0.01;
//
// The whole shader. No lighting, no emissive, no albedo - it writes only the
// refraction and transmission groups, so Spark never gives it a WriteDeferred
// technique and it never touches the G-buffer. See ns2_refract.hlsl.
//
// The 0.01 is in normalised screen UV, so the bend is one percent of the
// screen regardless of resolution.
//=============================================================================

HEADER
{
	Description = "NS2 Glass Refract";
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
	float g_flDistortionScale < UiGroup( ",0/,0/0" ); Default1( 0.01 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;

		float3 tsNormal = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv ) ) * g_flDistortionScale;

		float2 distortion = Ns2DistortionToScreen( tsNormal, i.vTangentUWs.xyz, i.vTangentVWs.xyz );

		return float4( Ns2Refract( i.vPositionSs.xy, i.vPositionSs.z, distortion, g_vTransmissionColor, 1.0 ), 1.0 );
	}
}
