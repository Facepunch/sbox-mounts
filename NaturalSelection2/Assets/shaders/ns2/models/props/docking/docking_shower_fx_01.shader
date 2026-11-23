//=============================================================================
// Port of NS2 ns2/models/props/docking/docking_shower_fx_01.surface_shader
//
//     const float3 tint = float3(1.1, 1.3, 1.5);
//
//     float4 texmask = tex2D(maskMap, input.texCoord);
//     float4 tex  = tex2D(emissiveMap, input.texCoord * float2(1.95, .67) + float2( time * .02, time * -.72));
//     float4 tex2 = tex2D(emissiveMap, input.texCoord * float2(2.7, .5)   + float2( time * -.1, time * -.83));
//
//     material.emissive = (tex.rgb + tex2.rgb) * tint * texmask.rgb;
//
// Falling water in the docking showers. One texture sampled twice, both squashed
// vertically (0.67 and 0.5) and stretched horizontally, then scrolled downward
// at nearly the same speed - the small difference is what keeps the two streams
// from locking together into an obvious repeat.
//
// The layers are ADDED, not multiplied, so each stream is visible on its own and
// the water stays continuous rather than breaking into droplets.
//
// The tint is barely above white and slightly blue - this is real water, not an
// energy effect, so it is only tinted rather than amplified.
//
// Only emissive is written, so this is the WriteEmissive technique alone:
// no G-buffer, no depth write, purely additive.
//=============================================================================

HEADER
{
	Description = "NS2 Docking Shower FX";
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
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( EmissiveMap, Srgb, 8, "None", "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( MaskMap,     Srgb, 8, "None", "_mask",     ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tMaskMap     < Channel( RGBA, Box( MaskMap ),     Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		const float3 tint = float3( 1.1, 1.3, 1.5 );

		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		float3 texmask = Tex2DS( g_tMaskMap, g_sWrap, uv ).rgb;
		float3 tex  = Tex2DS( g_tEmissiveMap, g_sWrap, uv * float2( 1.95, 0.67 ) + float2( t * 0.02, t * -0.72 ) ).rgb;
		float3 tex2 = Tex2DS( g_tEmissiveMap, g_sWrap, uv * float2( 2.7, 0.5 )   + float2( t * -0.1, t * -0.83 ) ).rgb;

		return float4( ( tex + tex2 ) * tint * texmask, 1.0 );
	}
}
