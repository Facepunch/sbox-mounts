//=============================================================================
// Port of NS2 ns2/models/props/origin/origin_lavapool.surface_shader
//
//     const float3 tint = float3(6, 1, .1);
//
//     // material.wsOffset = input.wsNormal * 0.03;   <- commented out in the original
//
//     float3 noise  = tex2D( glowpoints, input.texCoord * float2( 4, 4) + float2(time * .007, time * -0.08) ).rgb;
//     float3 noise2 = tex2D( noiseMap,   input.texCoord * float2( 2, 2) + float2(time * -.01, time * -.05) ).rgb;
//     float3 noise3 = tex2D( noiseMap,   input.texCoord * float2( 3, 3) + float2(time * .01, time * -.1) ).rgb;
//
//     material.emissive = ( noise + (noise2*noise3)) * 0.2 * tint;
//     material.opacity  = ( noise + noise2 ) * 1.5;
//
// Origin's lava pool. This is cinematics/vfx_materials/lavapool with exactly
// three edits, and all three are toning it down:
//
//   - the wsOffset line is commented out, so this one sits flush with its
//     geometry instead of floating 0.03m above it,
//   - emissive gains a 0.2 multiplier, so about a fifth the glow,
//   - opacity drops from 2.5 to 1.5.
//
// Origin's lava is background set dressing seen from a distance, where
// refinery's is walked right past, hence the much dimmer grade. Kept as a
// separate file rather than a parameterised shared one because Spark ships
// them as two shaders and the mount resolves by path.
//=============================================================================

HEADER
{
	Description = "NS2 Origin Lava Pool";
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
		// No wsOffset here - the displacement is commented out in the original.
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

		float3 emissive = ( noise + ( noise2 * noise3 ) ) * 0.2 * tint;

		// Spark assigns a float3 to material.opacity, which truncates to .x.
		// Not saturated - WriteEmissivePS returns material.opacity unclamped and the
		// pass blends src_alpha into an fp16 HDR target, so this is an additive gain.
		float opacity = ( noise.x + noise2.x ) * 1.5;

		return float4( emissive, opacity );
	}
}
