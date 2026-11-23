//=============================================================================
// Port of NS2 ns2/materials/unearthed/holo_emissive.surface_shader
//
//     const float ScanlineWidth = 0.75;
//     float Scanlines = saturate(floor(frac(input.wsPosition.y / ScanlineScale + (1-time)) + ScanlineWidth) + 0.7);
//     float4 tex = tex2D(emissiveMap, input.texCoord) * Scanlines;
//     material.emissive = tex.rgb * input.color.rgb;
//
// A holographic display panel with scanlines rolling up it.
//
// The scanline expression looks like a gradient but is a hard two-level switch.
// floor(frac(x) + 0.75) is 1 whenever frac(x) >= 0.25 and 0 otherwise, so
// saturate(that + 0.7) is either 1.0 or 0.7 - a 75%/25% duty square wave that
// dims the panel to 70% in the gaps rather than blanking it. ScanlineWidth is
// the duty cycle, not a distance.
//
// The band position comes from world height, not UV, so the scanlines line up
// across every surface using this material regardless of how each one is
// unwrapped, and they keep rolling even if the panel itself moves.
//
// Two conversions are needed. Spark is Y-up and s&box is Z-up, so wsPosition.y
// becomes vPositionWs.z. And Spark authors world distances in metres, so
// ScanlineScale is a period in metres and must be scaled by NS2_METRE -
// otherwise the bands come out 39x too tight to see as anything but noise.
//
// Only emissive is written, so this is the WriteEmissive technique alone:
// no G-buffer, no depth write, purely additive.
//=============================================================================

HEADER
{
	Description = "NS2 Holo Emissive";
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
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( EmissiveMap, Srgb, 8, "None", "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;

	float g_flScanlineScale < UiGroup( ",0/,0/0" ); Default1( 10.0 ); Range1( 0.01, 100 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		const float scanlineWidth = 0.75;

		float2 uv = i.vTextureCoords.xy;

		// Spark's Y-up world height is s&box's Z, and the period is in metres.
		float heightWs = i.vPositionWithOffsetWs.z + g_vCameraPositionWs.z;
		float phase = heightWs / ( g_flScanlineScale * NS2_METRE ) + ( 1.0 - g_flTime );

		// A 1.0 / 0.7 square wave, not a gradient - see the header.
		float scanlines = saturate( floor( frac( phase ) + scanlineWidth ) + 0.7 );

		float3 emissive = Tex2DS( g_tEmissiveMap, g_sWrap, uv ).rgb * scanlines * i.vColor.rgb;

		return float4( emissive, 1.0 );
	}
}
