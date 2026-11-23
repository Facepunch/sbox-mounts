//=============================================================================
// Port of NS2 ns2/shaders/glitch_hologram.surface_shader
//
//     float4 shift = float4pow( noise(float2(glitchSpeed*time, 2.0*glitchSpeed*time/25.0)), 8.0 )
//                  * float4(AMPLITUDE, AMPLITUDE, AMPLITUDE, 1.0);
//     c += rgbShift(p, shift);
//     float flicker = tex2D(flickerMap, float2(time * flickerSpeed, 0)).r;
//     float4 tex = tex2D(emissiveMap, input.texCoord + time * float2(xScroll, yScroll));
//     material.emissive = tex.rgb * input.color.rgb * c;
//     material.opacity  = tex.a * input.color.a * flicker;
//
// The hologram shader with a chromatic aberration glitch layered on top. The
// shift amount is sampled from emissiveMap itself at a time varying coordinate
// and raised to the 8th power so the stutter arrives in sudden bursts rather
// than as a smooth wobble; alpha is deliberately left out of the pow so it
// stays uniformly distributed and can pick the sign of the shift.
//
// Note that NS2 starts c at float4(0,0,0,1) and *adds* rgbShift, which itself
// returns w = 1, so c ends up (r, g, b, 2). The alpha is discarded by the
// float3 multiply, so only rgb matters.
//
// Emissive only, so this takes Spark's WriteEmissive technique: additive,
// premultiplied by opacity.
//=============================================================================

HEADER
{
	Description = "NS2 Glitch Hologram";
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
	CreateInputTexture2D( FlickerMap,  Linear, 8, "None", "_flicker",  ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tFlickerMap  < Channel( RGBA, Box( FlickerMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flXScroll      < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( -8, 8 ); >;
	float g_flYScroll      < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( -8, 8 ); >;
	float g_flFlickerSpeed < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 64 ); >;
	float g_flGlitchSpeed  < UiGroup( ",0/,0/0" ); Default1( 2.0 ); Range1( 0, 64 ); >;

	static const float AMPLITUDE = 0.0075;

	float3 RgbShift( float2 p, float4 shift )
	{
		shift *= 2.0 * shift.a - 1.0;

		float2 rs = float2( shift.r, -shift.g );
		float2 gs = float2( shift.g, -shift.b );
		float2 bs = float2( shift.b, -shift.r );

		return float3(
			Tex2DS( g_tEmissiveMap, g_sWrap, p + rs ).r,
			Tex2DS( g_tEmissiveMap, g_sWrap, p + gs ).g,
			Tex2DS( g_tEmissiveMap, g_sWrap, p + bs ).b );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 p = i.vTextureCoords.xy;

		float4 n = Tex2DS( g_tEmissiveMap, g_sWrap, float2( g_flGlitchSpeed * g_flTime, 2.0 * g_flGlitchSpeed * g_flTime / 25.0 ) );
		float4 shift = float4( pow( n.xyz, 8.0 ), n.w ) * float4( AMPLITUDE, AMPLITUDE, AMPLITUDE, 1.0 );

		float3 c = RgbShift( p, shift );

		float flicker = Tex2DS( g_tFlickerMap, g_sWrap, float2( g_flTime * g_flFlickerSpeed, 0.0 ) ).r;
		float4 tex = Tex2DS( g_tEmissiveMap, g_sWrap, i.vTextureCoords.xy + g_flTime * float2( g_flXScroll, g_flYScroll ) );

		float3 emissive = Ns2SrgbToLinear( saturate( tex.rgb * c ) ) * i.vColor.rgb;
		float opacity = tex.a * i.vColor.a * flicker;

		return float4( emissive, opacity );
	}
}
