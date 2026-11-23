//=============================================================================
// Port of NS2 ns2/models/props/docking/docking_shower_fx_puddle.surface_shader
//
//     const float3 tint = float3(4.1, 6.3, 8.5);
//
//     float4 texmask = tex2D(maskMap, input.texCoord);
//     float4 tex  = tex2D(emissiveMap, input.texCoord * float2(5.95, 2.67) + float2(time *  .03, time * -.25));
//     float4 tex2 = tex2D(emissiveMap, input.texCoord * float2(4.7,  1.5 ) + float2(time * -.02, time * -.36));
//     float4 tex3 = tex2D(emissiveMap, input.texCoord * float2(3.7,  1.1 ) + float2(time * -.01, time * -.15));
//
//     material.emissive = ((tex.rgb + tex2.rgb) * tex3.rgb * tint * texmask.rgb);
//
// The water running off Docking's shower prop. The simplest shader in the
// family: one scrolling map sampled three times at different rates, two summed
// and the third used as a multiplier so the streaks beat against each other,
// all masked by a static maskMap that confines the effect to the puddle.
//
// tint is a cold blue-white (4.1, 6.3, 8.5) well above 1, so the highlights
// bloom like wet specular rather than reading as a flat texture.
//
// twoSided in the original, so culling is disabled - the puddle plane is
// visible from underneath.
//
// Emissive only, so no G-buffer pass: no depth write, no occlusion, purely
// additive over the lit scene.
//=============================================================================

HEADER
{
	Description = "NS2 Docking Shower FX Puddle";
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

	// twoSided in the original.
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

	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( MaskMap,     Linear, 8, "None", "_mask",     ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tMaskMap     < Channel( RGBA, Box( MaskMap ),     Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		const float3 tint = float3( 4.1, 6.3, 8.5 );

		float3 texmask = Ns2SrgbToLinear( Tex2DS( g_tMaskMap,     g_sWrap, uv ).rgb );
		float3 tex     = Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, uv * float2( 5.95, 2.67 ) + float2( t *  0.03, t * -0.25 ) ).rgb );
		float3 tex2    = Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, uv * float2( 4.70, 1.50 ) + float2( t * -0.02, t * -0.36 ) ).rgb );
		float3 tex3    = Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, uv * float2( 3.70, 1.10 ) + float2( t * -0.01, t * -0.15 ) ).rgb );

		float3 emissive = ( tex + tex2 ) * tex3 * tint * texmask;

		// The original never writes opacity, so it keeps Spark's default of 1.
		return float4( emissive, 1.0 );
	}
}
