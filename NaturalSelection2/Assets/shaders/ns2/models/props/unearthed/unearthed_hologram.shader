//=============================================================================
// Port of NS2 ns2/models/props/unearthed/unearthed_hologram.surface_shader
//
//     const float3 DangerColor = float3(1,0,0);
//     const float3 TextColor   = float3(1,1,1);
//     const float3 HoloColor   = float3(0,0.5,1);
//     const float2 UVSectionRed   = float2(0.745,0.38);
//     const float2 UVSectionWhite = float2(0.88,0.6);
//     const float intensity = 3.0;
//
//     float ColorsectionWhite = floor(UV.r + UVSectionWhite.r) * floor(1 - UV.g + UVSectionWhite.g);
//     float ColorsectionRed   = floor(UV.r + UVSectionRed.r)   * floor(UV.g + UVSectionRed.g);
//     float3 ColorWhite = lerp(HoloColor, TextColor, ColorsectionWhite);
//     float3 Color      = lerp(ColorWhite, DangerColor, ColorsectionRed);
//
//     float Scanlines = saturate(floor(frac(input.wsPosition.y / ScanlineScale + (1-time)) + 0.75) + 0.7);
//
//     material.emissive          = Color.rgb;
//     material.transmissionColor = tex2D(AlphaTexture, UV).rgb * Scanlines * intensity;
//     material.opacity           = tex2D(AlphaTexture, UV).rgb * Scanlines * Opacity;
//
// A translucent warning hologram in ns2_unearthed.
//
// The colour is picked by UV region rather than by a texture. floor(UV.r+0.88)
// is 1 only once UV.r passes 0.12, and floor(1-UV.g+0.6) is 1 only while UV.g
// is below 0.6, so the two products carve rectangular blocks out of the panel:
// one region tinted white for text, another red for the danger strip, and the
// cyan HoloColor everywhere else. It is a cheap way to author three colours
// into one greyscale texture.
//
// The scanline term is the same trick as holo_emissive: floor(frac(x)+0.75) is
// a hard 0/1 switch, so this is a 75%/25% duty square wave that dims to 70%
// rather than a gradient. It is driven by world height so bands line up across
// every hologram in the map. Spark is Y-up (wsPosition.y -> vPositionWs.z) and
// authors the period in metres, hence the NS2_METRE scale.
//
// Compositing: this material is <param name="translucent">true</param>, so
// Spark does not light it. Deferred.hlsl composites it as
//
//     dst = dst * lerp(1, transmissionColor, opacity) + emissive * opacity
//
// (the specular/environment term is zero here, since no specular is written).
// transmissionColor goes up to 3.0, so the hologram BRIGHTENS what is behind it
// rather than tinting it down. A single-pass ONE/ONE additive blend cannot
// express that multiply, so the scene is sampled from the framebuffer copy and
// the whole composite is evaluated here, written with ONE/ZERO. That reproduces
// Spark's result exactly instead of approximating it.
//
// wsOffset and the osPosition distance falloff are commented out in the
// original. Left out.
//=============================================================================

HEADER
{
	Description = "NS2 Unearthed Hologram";
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
	#include "ns2/ns2_refract.hlsl"

	BoolAttribute( bWantsFBCopyTexture, true );

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, NONE );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ZERO );
	RenderState( BlendOp, ADD );
	RenderState( SrcBlendAlpha, ONE );
	RenderState( DstBlendAlpha, ZERO );
	RenderState( BlendOpAlpha, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlphaTexture, Srgb, 8, "None", "_alpha", ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tAlphaTexture < Channel( RGBA, Box( AlphaTexture ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;

	float g_flScanlineScale < UiGroup( ",0/,0/0" ); Default1( 10.0 ); Range1( 0.01, 100 ); >;
	float g_flOpacity < UiGroup( ",0/,0/0" ); Default1( 0.5 ); Range1( 0, 1 ); >;
	float g_flDecals < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		const float3 dangerColor = float3( 1.0, 0.0, 0.0 );
		const float3 textColor = float3( 1.0, 1.0, 1.0 );
		const float3 holoColor = float3( 0.0, 0.5, 1.0 );
		const float2 uvSectionRed = float2( 0.745, 0.38 );
		const float2 uvSectionWhite = float2( 0.88, 0.6 );
		const float intensity = 3.0;
		const float scanlineWidth = 0.75;

		float2 uv = i.vTextureCoords.xy;

		// Rectangular UV regions select the three panel colours.
		float sectionWhite = floor( uv.r + uvSectionWhite.r ) * floor( 1.0 - uv.g + uvSectionWhite.g );
		float sectionRed = floor( uv.r + uvSectionRed.r ) * floor( uv.g + uvSectionRed.g );

		float3 color = lerp( lerp( holoColor, textColor, sectionWhite ), dangerColor, sectionRed );

		// Spark's Y-up world height is s&box's Z, and the period is in metres.
		float heightWs = i.vPositionWithOffsetWs.z + g_vCameraPositionWs.z;
		float phase = heightWs / ( g_flScanlineScale * NS2_METRE ) + ( 1.0 - g_flTime );

		// A 1.0 / 0.7 square wave, not a gradient.
		float scanlines = saturate( floor( frac( phase ) + scanlineWidth ) + 0.7 );

		float3 alpha = Tex2DS( g_tAlphaTexture, g_sWrap, uv ).rgb * scanlines;
		float3 transmissionColor = alpha * intensity;

		// Spark truncates the float3 assigned to opacity to .x.
		float opacity = ( alpha * g_flOpacity ).x;

		// dst * lerp(1, transmissionColor, opacity) + emissive * opacity,
		// evaluated here because a single additive blend cannot do the multiply.
		float3 scene = Ns2WarpScene( i.vPositionSs.xy, i.vPositionSs.z, float2( 0.0, 0.0 ) );
		float3 result = scene * lerp( float3( 1.0, 1.0, 1.0 ), transmissionColor, opacity ) + color * opacity;

		return float4( result, 1.0 );
	}
}
