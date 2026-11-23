//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/lavafall.surface_shader
//
//     const float3 tint = float3(.6, .1, .01);
//
//     material.wsOffset = input.wsNormal * 0.03;
//
//     float3 ash    = tex2D( glowpoints, input.texCoord * float2( 0.8, .7 ) + float2(time * -.025, time * -.35) ).rgb;
//     float3 noise  = tex2D( glowpoints, input.texCoord * float2( 0.2, .5 ) + float2(time *  .025, time * -0.25) ).rgb;
//     float3 noise2 = tex2D( noiseMap,   input.texCoord * float2( 0.3, 1.3) + float2(time *  -.01, time * -.5 ) ).rgb;
//     float3 noise3 = tex2D( glowpoints, input.texCoord * float2( 0.6, 2.1) + float2(time *  -.02, time * -.8 ) ).rgb;
//
//     material.emissive = ( noise * noise2 + ash + noise3 ) * tint;
//     material.opacity  = ( (noise + noise2) + noise3 + ash ) * 10;
//
// The falling counterpart to lavapool. Same construction, retuned for vertical
// flow: every layer scrolls hard in -V (up to -0.8/s against the pool's -0.1/s)
// and the tiling is stretched along V, so the noise smears into streaks rather
// than reading as cells.
//
// tint is (.6, .1, .01) - a tenth of lavapool's - because four layers are being
// summed here rather than two, so the sum is roughly an order of magnitude
// larger before tinting.
//
// wsOffset again pushes 0.03 metres along the normal, converted to inches.
//
// The original sets alphaTest, but it writes no albedo, so in Spark it gets no
// G-buffer pass and is composited additively - and opacity is multiplied by 10,
// so the test only ever clips texels where all four layers are black. The
// additive blend already contributes nothing for those texels, so no explicit
// clip is needed here and none is emitted.
//=============================================================================

HEADER
{
	Description = "NS2 Lava Fall";
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

		const float3 tint = float3( 0.6, 0.1, 0.01 );

		float3 ash    = Ns2SrgbToLinear( Tex2DS( g_tGlowpoints, g_sWrap, uv * float2( 0.8, 0.7 ) + float2( t * -0.025, t * -0.35 ) ).rgb );
		float3 noise  = Ns2SrgbToLinear( Tex2DS( g_tGlowpoints, g_sWrap, uv * float2( 0.2, 0.5 ) + float2( t *  0.025, t * -0.25 ) ).rgb );
		float3 noise2 = Ns2SrgbToLinear( Tex2DS( g_tNoiseMap,   g_sWrap, uv * float2( 0.3, 1.3 ) + float2( t * -0.01,  t * -0.5  ) ).rgb );
		float3 noise3 = Ns2SrgbToLinear( Tex2DS( g_tGlowpoints, g_sWrap, uv * float2( 0.6, 2.1 ) + float2( t * -0.02,  t * -0.8  ) ).rgb );

		float3 emissive = ( noise * noise2 + ash + noise3 ) * tint;

		// Spark assigns a float3 to material.opacity, which truncates to .x.
		float opacity = ( ( noise.x + noise2.x ) + noise3.x + ash.x ) * 10.0;

		// Deliberately NOT saturated. Spark's WriteEmissivePS ends in
		// "return float4(material.emissive, material.opacity)" with no clamp, and the
		// pass blends src_color_factor = src_alpha into an fp16 HDR target, so an alpha
		// of 10+ is a genuine 10x emissive multiplier rather than a coverage value.
		// Clamping it here made the fall ~10x too dim and let the wall behind show through.
		return float4( emissive, opacity );
	}
}
