//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/2em_scroll_vtxcolor.surface_shader
//
//     <param name="twoSided">true</param>
//
//     float4 emiss1 = tex2D(emissiveMap1, input.texCoord * float2(em1Coord.r, em1Coord.g) + float2( time * em1Coord.b, time * em1Coord.a));
//     float4 emiss2 = tex2D(emissiveMap2, input.texCoord * float2(em2Coord.r, em2Coord.g) + float2( time * em2Coord.b, time * em2Coord.a));
//     material.emissive = (emiss1.rgb * emiss2.rgb) * tint * input.color.rgb;
//     material.opacity  = input.color.a;
//
// Two emissive layers scrolling over each other at independent rates and UV
// scales, modulated by the particle/mesh vertex colour. em1Coord/em2Coord pack
// (uvScale.xy, scrollSpeed.zw) into one float4 each.
//
// Only emissive + opacity are written, so this runs Spark's WriteEmissive
// technique: additive, premultiplied by opacity.
//
// Both maps are srgb="true", so Spark's samplers decode each one before the
// multiply - unlike the non-srgb texture-product shaders, where the product is
// formed in gamma space. tint is an authored constant and stays raw.
//
// The NS2 source also requests osPosition, wsPosition, wsNormal and vsNormal
// and then uses none of them. Dropped.
//
// twoSided => no culling. See model_alpha_double.shader for the full rationale.
//=============================================================================

HEADER
{
	Description = "NS2 Two Emissive Scroll Vertex Color";
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

	RenderState( CullMode, NONE );
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

	CreateInputTexture2D( EmissiveMap1, Linear, 8, "None", "_emissive", ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( EmissiveMap2, Linear, 8, "None", "_emissive", ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );

	Texture2D g_tEmissiveMap1 < Channel( RGBA, Box( EmissiveMap1 ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap2 < Channel( RGBA, Box( EmissiveMap2 ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 g_vTint     < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 1.0, 1.0 ); >;
	float4 g_vEm1Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vEm2Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;

		float2 uv1 = uv * g_vEm1Coord.xy + float2( g_flTime * g_vEm1Coord.z, g_flTime * g_vEm1Coord.w );
		float2 uv2 = uv * g_vEm2Coord.xy + float2( g_flTime * g_vEm2Coord.z, g_flTime * g_vEm2Coord.w );

		float3 emiss1 = Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap1, g_sWrap, uv1 ).rgb );
		float3 emiss2 = Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap2, g_sWrap, uv2 ).rgb );

		float3 emissive = emiss1 * emiss2 * g_vTint.rgb * i.vColor.rgb;
		float opacity = i.vColor.a;

		return float4( emissive, opacity );
	}
}
