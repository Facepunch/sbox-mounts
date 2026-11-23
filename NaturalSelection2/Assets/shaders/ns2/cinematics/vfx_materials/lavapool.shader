//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/lavapool.surface_shader
//
//     const float3 tint = float3(6, 1, .1);
//
//     material.wsOffset = input.wsNormal * 0.03;
//
//     float3 noise  = tex2D( glowpoints, input.texCoord * float2( 4, 4) + float2(time * .007, time * -0.08) ).rgb;
//     float3 noise2 = tex2D( noiseMap,   input.texCoord * float2( 2, 2) + float2(time * -.01, time * -.05) ).rgb;
//     float3 noise3 = tex2D( noiseMap,   input.texCoord * float2( 3, 3) + float2(time * .01, time * -.1) ).rgb;
//
//     material.emissive = ( noise + (noise2*noise3)) * tint;
//     material.opacity  = ( noise + noise2 ) * 2.5;
//
// The refinery lava pool surface. Used by models/props/refinery/refinery_lavapool_01.
//
// Unlike the forcefield family this ADDS its first noise layer rather than
// multiplying everything, so there is always a base glow, and only the
// noise2*noise3 cross term flickers. That is what gives lava its slow churn
// instead of sparse blinking cells.
//
// tint (6, 1, .1) is a >1 HDR orange, so the surface blooms.
//
// wsOffset pushes the surface 0.03 metres along its own normal. Spark works in
// metres and s&box in inches, so that is scaled on the way in - otherwise the
// pool would sit 33x too close to the geometry it is meant to float above.
//
// opacity deliberately runs well over 1 (x2.5). It is not a transparency here:
// the material writes no albedo, so Spark composites it with WriteEmissive's
// src_alpha/one blend into an fp16 HDR target. WriteEmissivePS returns
// float4(material.emissive, material.opacity) with no clamp, so the alpha is an
// unclamped additive gain - it must NOT be saturated here or the result is up to
// 2.5x too dim and whatever sits behind shows through.
//=============================================================================

HEADER
{
	Description = "NS2 Lava Pool";
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
		PixelInput o = ProcessVertex( v );

		// Spark: material.wsOffset = input.wsNormal * 0.03 (metres -> inches).
		o.vPositionWs.xyz += o.vNormalWs.xyz * ( 0.03 * 40.0 );
		o.vPositionPs = Position3WsToPs( o.vPositionWs.xyz );

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

	CreateInputTexture2D( Glowpoints, Linear, 8, "None", "_glow",  ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NoiseMap,   Linear, 8, "None", "_noise", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tGlowpoints < Channel( RGBA, Box( Glowpoints ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNoiseMap   < Channel( RGBA, Box( NoiseMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		const float3 tint = float3( 6.0, 1.0, 0.1 );

		float3 noise  = Ns2SrgbToLinear( Tex2DS( g_tGlowpoints, g_sWrap, uv * float2( 4.0, 4.0 ) + float2( t * 0.007, t * -0.08 ) ).rgb );
		float3 noise2 = Ns2SrgbToLinear( Tex2DS( g_tNoiseMap,   g_sWrap, uv * float2( 2.0, 2.0 ) + float2( t * -0.01, t * -0.05 ) ).rgb );
		float3 noise3 = Ns2SrgbToLinear( Tex2DS( g_tNoiseMap,   g_sWrap, uv * float2( 3.0, 3.0 ) + float2( t * 0.01, t * -0.1 ) ).rgb );

		float3 emissive = ( noise + ( noise2 * noise3 ) ) * tint;

		// Spark assigns a float3 to material.opacity, which truncates to .x.
		float opacity = ( noise.x + noise2.x ) * 2.5;

		return float4( emissive, opacity );
	}
}
