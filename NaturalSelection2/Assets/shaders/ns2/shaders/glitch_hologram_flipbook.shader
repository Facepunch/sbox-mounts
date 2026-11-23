//=============================================================================
// Port of NS2 core/shaders/Glitch_hologram_flipbook.surface_shader
//
// A flipbook hologram with an RGB-split glitch on top. Three things are going
// on, and two of them are broken in the original in ways that are load bearing.
//
// 1. The flipbook.
//
//     float timesteps = lerp(floor(time*animationspeed), time*animationspeed, smoothBlending);
//     float substeps  = timesteps - frac(timesteps);           // == floor(timesteps)
//     float2 sub1 = float2(fmod(substeps,   frames.r), floor(substeps   * framesdiv.r));
//     float2 sub2 = float2(fmod(1+substeps, frames.r), floor((1+substeps) * framesdiv.r));
//     float3 SubUVlerp = lerp(tex2D(emissiveMap, UV1).rgb, tex2D(emissiveMap, UV2).rgb, frac(timesteps));
//
//    Standard atlas cell addressing with a cross fade to the next frame.
//    smoothBlending picks between stepping frame to frame and sliding
//    continuously. Note the row index uses framesdiv.r (1/columns) for both
//    sub1 and sub2, which is correct, but the cell index is never wrapped
//    against the total frame count - a long enough run walks off the bottom of
//    the atlas.
//
// 2. The distance blend, which does nothing.
//
//     float3 screenblend = lerp(tex, SubUVlerp, distanceblend(0, 0, input.vsPosition));
//     distanceblend(range, offset, p) = saturate((length(p) - offset) / range);
//
//    Called with range = 0, so this divides by zero. length(p) is positive for
//    any real vertex, giving +INF, and saturate clamps that to exactly 1. So
//    the lerp always returns SubUVlerp and the `tex` sample is discarded. Ported
//    as the constant it evaluates to rather than reproducing the divide.
//
// 3. The RGB split.
//
//     shift = float4pow(noise(float2(glitchSpeed*time, glitchSpeed*time*2/25)), 8) * float4(A,A,A,1);
//     rgbShift: shift *= 2*shift.a - 1; then r/g/b are sampled at p+(r,-g), p+(g,-b), p+(b,-r).
//
//    The emissive texture is abused as a noise source: a single texel read at a
//    time-varying coordinate drives the offsets. Raising it to the 8th power
//    makes the glitch sudden and rare instead of a constant shimmer, since only
//    values near 1 survive. The alpha channel is deliberately left out of that
//    power (see float4pow) because it is repurposed as a sign: 2*a-1 flips the
//    whole shift between positive and negative so the split does not favour one
//    direction.
//
// c starts at float4(0,0,0,1) and rgbShift is added to it, so c.rgb is the
// split sample and c.a becomes 2 - unused, since Spark truncates the float4 to
// a float3 when it multiplies into emissive.
//
// Only emissive and opacity are written: WriteEmissive alone, no G-buffer, no
// depth write, and opacity is an unclamped additive gain rather than coverage.
//=============================================================================

HEADER
{
	Description = "NS2 Glitch Hologram Flipbook";
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
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );
	RenderState( SrcBlendAlpha, SRC_ALPHA );
	RenderState( DstBlendAlpha, ONE );
	RenderState( BlendOpAlpha, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( EmissiveMap, Srgb,   8, "None", "_emissive", ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( FlickerMap,  Linear, 8, "None", "_flicker",  ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tFlickerMap  < Channel( RGBA, Box( FlickerMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flAnimationspeed < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 64 ); >;
	float g_flHorizontalFrames < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 1, 64 ); >;
	float g_flVerticalFrames < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 1, 64 ); >;
	float g_flFlickerSpeed < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 64 ); >;
	float g_flGlitchSpeed < UiGroup( ",0/,0/0" ); Default1( 2.0 ); Range1( 0, 64 ); >;
	float g_flHiddenAmount < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flEmissiveMod < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;
	float g_flSmoothBlending < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 1 ); >;

	// Alpha is left out of the power on purpose - it is reused as a sign below.
	float4 Float4Pow( float4 v, float p )
	{
		return float4( pow( v.x, p ), pow( v.y, p ), pow( v.z, p ), v.w );
	}

	float4 RgbShift( float2 p, float4 shift )
	{
		shift *= 2.0 * shift.a - 1.0;

		float r = Tex2DS( g_tEmissiveMap, g_sWrap, p + float2( shift.r, -shift.g ) ).r;
		float g = Tex2DS( g_tEmissiveMap, g_sWrap, p + float2( shift.g, -shift.b ) ).g;
		float b = Tex2DS( g_tEmissiveMap, g_sWrap, p + float2( shift.b, -shift.r ) ).b;

		return float4( r, g, b, 1.0 );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		const float amplitude = 0.0075;

		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		float timestephard = floor( t * g_flAnimationspeed );
		float timestepsoft = t * g_flAnimationspeed;
		float timesteps = lerp( timestephard, timestepsoft, g_flSmoothBlending );

		float2 frames = float2( floor( g_flHorizontalFrames ), floor( g_flVerticalFrames ) );
		float2 framesdiv = float2( 1.0, 1.0 ) / frames;

		float substeps = timesteps - frac( timesteps );

		float2 sub1 = float2( fmod( substeps, frames.r ), floor( substeps * framesdiv.r ) );
		float2 sub2 = float2( fmod( 1.0 + substeps, frames.r ), floor( ( 1.0 + substeps ) * framesdiv.r ) );

		float2 uv1 = ( uv + sub1 ) * framesdiv;
		float2 uv2 = ( uv + sub2 ) * framesdiv;

		float3 subUvLerp = lerp( Tex2DS( g_tEmissiveMap, g_sWrap, uv1 ).rgb,
			Tex2DS( g_tEmissiveMap, g_sWrap, uv2 ).rgb, frac( timesteps ) );

		// The emissive texture doubles as the noise source for the glitch.
		float4 noise = Tex2DS( g_tEmissiveMap, g_sWrap, float2( g_flGlitchSpeed * t, 2.0 * g_flGlitchSpeed * t / 25.0 ) );
		float4 shift = Float4Pow( noise, 8.0 ) * float4( amplitude, amplitude, amplitude, 1.0 );

		float4 c = float4( 0.0, 0.0, 0.0, 1.0 ) + RgbShift( uv2, shift );

		// distanceblend(0, 0, ...) divides by zero and saturates to 1, so the
		// blend always resolves to subUvLerp - see the header.
		float3 screenblend = subUvLerp;

		float flicker = Tex2DS( g_tFlickerMap, g_sWrap, float2( t * g_flFlickerSpeed, 0.0 ) ).r;
		float4 tex = Tex2DS( g_tEmissiveMap, g_sWrap, uv + t );

		float3 emissive = ( screenblend * c.rgb ) * g_flEmissiveMod;
		float opacity = tex.a * i.vColor.a * flicker * ( 1.0 - g_flHiddenAmount );

		return float4( emissive, opacity );
	}
}
