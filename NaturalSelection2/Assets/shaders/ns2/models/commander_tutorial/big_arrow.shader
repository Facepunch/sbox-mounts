//=============================================================================
// Port of NS2 ns2/models/commander_tutorial/big_arrow.surface_shader
//
//     const float  scanlinesBlackLevel  = 0.25;
//     const float  scanlinesScaleFactor = 0.25;
//     const float2 scanlinesSpeed       = float2(0.01, 0.025);
//
//     float2 vsPosition = normalize(input.vsPosition).xy;
//
//     float edgeGlowFact  = tex2D(glowMap, input.texCoord).r;
//     float scanlinesFact = edgeGlowFact * (1.0-scanlinesBlackLevel) + scanlinesBlackLevel;
//     float2 scanlinesCoords  = float2(vsPosition.x + time * scanlinesSpeed.x, vsPosition.y + time * scanlinesSpeed.y) * scanlinesScaleFactor;
//     float2 scanlinesCoords2 = float2(vsPosition.x, vsPosition.y) * scanlinesScaleFactor * 2;
//
//     float scanlines1 = tex2D(scanLinesMap, scanlinesCoords).r;
//     float scanlines2 = tex2D(scanLinesMap, scanlinesCoords2).r;
//     float3 scanlines = pow(scanlines1 * scanlines2, 0.5) * tintColor * scanlinesFact;
//
//     float3 edgeGlow = tintColor * edgeGlowFact;
//
//     float flashScroll = saturate(sin(input.osPosition.z - (time*5)) * 10 - 9);
//     flashScroll = flashScroll * flashScroll * flashScroll * flashScroll;
//
//     float opacityFactor = fade * fade;
//     material.emissive = opacityFactor * (1.0-((1.0-scanlines) * (1.0-edgeGlow) * (1.0-flashScroll))); //screen them together
//
// The big holographic arrow in the commander tutorial. Three layers screened
// together: a static edge glow from glowMap.r, a screen-locked scanline moire
// (two copies of the same map at different scales, one scrolling), and a
// bright pulse that travels along the arrow's length.
//
// Two Spark-only inputs are substituted:
//
//  * input.vsPosition - view-space fragment position, normalised and used only
//    to generate screen-locked scanline UVs. s&box exposes g_matWorldToView
//    (core/shaders/common.fxc), so the camera-relative world position is
//    transformed with Position3WsToVs. The exact axis convention does not
//    matter here: any consistent screen-aligned basis produces the same moire,
//    at worst mirrored.
//
//  * input.osPosition.z - object-space position along the arrow. NS2 works in
//    metres and the importer scales by 40, so the incoming s&box object-space
//    position is divided by NS2_UNITS_PER_METRE (same as exominigunview).
//
// The `sin(...) * 10 - 9` then saturate then ^4 turns the sine into a very
// narrow travelling spike - roughly 4% of each wavelength - which is the
// flash that runs up the arrow.
//
// Neither map is srgb-flagged, and both are read as single-channel masks, so
// no decode is involved. tintColor is an authored constant and stays linear.
//=============================================================================

HEADER
{
	Description = "NS2 Big Arrow";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#ifndef S_ALPHA_TEST
	#define S_ALPHA_TEST 0
	#endif

	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 0
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
	float3 vPositionOs : TEXCOORD14;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		i.vPositionOs = v.vPositionOs.xyz;
		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	static const float NS2_UNITS_PER_METRE = 40.0;

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( GlowMap,      Linear, 8, "None", "_mask", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( ScanLinesMap, Linear, 8, "None", "_mask", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tGlowMap      < Channel( RGBA, Box( GlowMap ),      Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tScanLinesMap < Channel( RGBA, Box( ScanLinesMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 g_vTintColor < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 1.0, 1.0 ); >;
	float  g_flFade     < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		const float scanlinesBlackLevel = 0.25;
		const float scanlinesScaleFactor = 0.25;
		const float2 scanlinesSpeed = float2( 0.01, 0.025 );

		float2 uv = i.vTextureCoords.xy;

		float3 osPosition = i.vPositionOs / NS2_UNITS_PER_METRE;
		float2 vsPosition = normalize( Position3WsToVs( i.vPositionWithOffsetWs.xyz ).xyz ).xy;

		float edgeGlowFact = Tex2DS( g_tGlowMap, g_sWrap, uv ).r;
		float scanlinesFact = edgeGlowFact * ( 1.0 - scanlinesBlackLevel ) + scanlinesBlackLevel;

		float2 scanlinesCoords  = ( vsPosition + g_flTime * scanlinesSpeed ) * scanlinesScaleFactor;
		float2 scanlinesCoords2 = vsPosition * scanlinesScaleFactor * 2.0;

		float scanlines1 = Tex2DS( g_tScanLinesMap, g_sWrap, scanlinesCoords ).r;
		float scanlines2 = Tex2DS( g_tScanLinesMap, g_sWrap, scanlinesCoords2 ).r;
		float3 scanlines = pow( scanlines1 * scanlines2, 0.5 ) * g_vTintColor.rgb * scanlinesFact;

		float3 edgeGlow = g_vTintColor.rgb * edgeGlowFact;

		float flashScroll = saturate( sin( osPosition.z - ( g_flTime * 5.0 ) ) * 10.0 - 9.0 );
		flashScroll = flashScroll * flashScroll;
		flashScroll = flashScroll * flashScroll;

		float opacityFactor = g_flFade * g_flFade;

		m.Albedo = 0.0;
		m.Emission = opacityFactor * ( 1.0 - ( ( 1.0 - scanlines ) * ( 1.0 - edgeGlow ) * ( 1.0 - flashScroll ) ) );

		m.Opacity = 1.0;
		m.Roughness = 1.0;
		m.Metalness = 0.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = i.vNormalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
