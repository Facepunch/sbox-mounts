//=============================================================================
// Port of NS2 ns2/models/props/refinery/docking_generator_01_anim.surface_shader
//
//     float4 tex  = tex2D(emissiveMap, input.texCoord * float2(1 , 2.67) + float2( time * -5, time * -.8));
//     float4 tex2 = tex2D(emissiveMap, input.texCoord * float2(0.7 , 1.5) + float2( time * 6.37, time * .7));
//
//     material.emissive = tex.rgb + tex2.rgb * 3 * input.color.rgb;
//     material.opacity  = tex.a * input.color.a;
//
// The animated arc sheet that sits over the generator. Two copies of one
// texture scroll hard in opposite directions along X, which reads as
// electricity snapping back and forth rather than a sheet sliding past.
//
// Note the precedence: only the SECOND layer is multiplied by 3 and by the
// vertex colour, the first is added raw. That is not a typo in the original,
// it gives a constant base arc with a brighter tinted flicker on top.
//
// Only emissive and opacity are written, so Spark builds only the
// WriteEmissive technique - no G-buffer, no depth write, no occlusion. That
// pass blends src_alpha/one into an fp16 target with no clamp on the alpha, so
// opacity is an additive gain rather than coverage and must not be saturated.
//=============================================================================

HEADER
{
	Description = "NS2 Docking Generator Anim";
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
		PixelInput o = ProcessVertex( v );
		o.vColor = v.vColor;
		return FinalizeVertex( o );
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

	CreateInputTexture2D( EmissiveMap, Srgb, 8, "None", "_emissive", ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		float4 tex  = Tex2DS( g_tEmissiveMap, g_sWrap, uv * float2( 1.0, 2.67 ) + float2( t * -5.0, t * -0.8 ) );
		float4 tex2 = Tex2DS( g_tEmissiveMap, g_sWrap, uv * float2( 0.7, 1.5 )  + float2( t * 6.37, t * 0.7 ) );

		// Only the second layer takes the x3 and the vertex colour.
		float3 emissive = tex.rgb + tex2.rgb * 3.0 * i.vColor.rgb;
		float opacity = tex.a * i.vColor.a;

		return float4( emissive, opacity );
	}
}
