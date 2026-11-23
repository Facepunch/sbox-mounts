//=============================================================================
// Port of NS2 ns2/models/hologram_two_sided.surface_shader
//
//     <param name="twoSided">true</param>
//
//     float flicker = tex2D(flickerMap, float2(time * flickerSpeed, 0)).r;
//     float4 tex = tex2D(emissiveMap, input.texCoord + time * float2(xScroll, yScroll));
//     material.emissive = tex.rgb * input.color.rgb * emissiveMod;
//     material.opacity  = tex.a * input.color.a * flicker * emissiveMod;
//
// The double-sided variant of models/hologram. flickerMap is sampled as a 1D
// strip at v=0, so it is a hand-authored flicker envelope scrubbed at
// flickerSpeed; wrap addressing loops it.
//
// emissiveMod scales opacity as well as colour, which on an additive surface
// means it is applied twice. That is the NS2 behaviour and it is what makes
// hologram fade-outs fall off faster than linearly.
//
// emissiveMap is srgb="true"; flickerMap is explicitly srgb="false".
//
// twoSided => no culling. See model_alpha_double.shader for the rationale.
//=============================================================================

HEADER
{
	Description = "NS2 Hologram Two Sided";
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

	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissive", ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( FlickerMap,  Linear, 8, "None", "_mask",     ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tFlickerMap  < Channel( RGBA, Box( FlickerMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flXScroll      < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( -4, 4 ); >;
	float g_flYScroll      < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( -4, 4 ); >;
	float g_flFlickerSpeed < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 16 ); >;
	float g_flEmissiveMod  < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float flicker = Tex2DS( g_tFlickerMap, g_sWrap, float2( g_flTime * g_flFlickerSpeed, 0.0 ) ).r;

		float2 uv = i.vTextureCoords.xy + g_flTime * float2( g_flXScroll, g_flYScroll );
		float4 tex = Tex2DS( g_tEmissiveMap, g_sWrap, uv );

		float3 emissive = Ns2SrgbToLinear( tex.rgb ) * i.vColor.rgb * g_flEmissiveMod;
		float opacity = tex.a * i.vColor.a * flicker * g_flEmissiveMod;

		return float4( emissive, opacity );
	}
}
