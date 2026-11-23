//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/ghoststructure.surface_shader
//
//     const float3 tint = float3(2, 5, 5);
//
//     float3 scan     = tex2D( scanMap, input.wsPosition.y * float2(2, .25) + float2(time, time * -1) ).rgb;
//     float3 circuit1 = tex2D( glowcircuitMap, input.texCoord * 12 ).rgb;
//     float3 circuit2 = tex2D( circuitMap, input.texCoord * float2(5, 4) ).rgb;
//     float3 noise    = tex2D( noiseMap, input.texCoord * .4 + float2(0, time * .52) ).rgb;
//     float  fresnel  = pow(1 - max(-input.vsNormal.z, 0), 4);
//
//     material.emissive = float3(0, 0.03, 0.7)
//                       + ( circuit2 * scan + scan * 0.05 ) * tint
//                       + ( circuit1 * fresnel * noise ) * 2;
//
// The blue "ghost" preview a commander sees before a structure is placed.
//
// Two Spark-only inputs are substituted:
//
//  * input.vsNormal.z - in Spark's view space a camera-facing normal gives
//    -vsNormal.z == 1, so `max(-vsNormal.z, 0)` is just N dot V. Written here
//    as an explicit world-space N dot V, which is convention-independent and
//    avoids relying on s&box's view basis matching Spark's.
//
//  * input.wsPosition.y - NS2 world height, in metres. The importer maps NS2
//    (x, y, z) onto s&box (z, x, y) at 40 units per metre, so NS2's y is
//    s&box's world z. vPositionWithOffsetWs is camera-relative, hence the
//    g_vCameraPositionWs add before the divide.
//
// scanMap is sampled with a scalar height driving both axes, so the scan is a
// horizontal band pattern that rises with world height and scrolls in opposite
// directions on u and v.
//
// All four maps are srgb="true" and are decoded. The float3(0, 0.03, 0.7) base
// and the tint are authored constants and stay linear (see ns2.hlsl notes).
//
// osPosition / wsNormal / vsNormal / twoSided=false are requested by the NS2
// source; only vsNormal is actually read.
//=============================================================================

HEADER
{
	Description = "NS2 Ghost Structure";
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
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	static const float NS2_UNITS_PER_METRE = 40.0;

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( ScanMap,        Linear, 8, "None", "_color", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( CircuitMap,     Linear, 8, "None", "_color", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( GlowcircuitMap, Linear, 8, "None", "_color", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NoiseMap,       Linear, 8, "None", "_color", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tScanMap        < Channel( RGBA, Box( ScanMap ),        Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tCircuitMap     < Channel( RGBA, Box( CircuitMap ),     Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tGlowcircuitMap < Channel( RGBA, Box( GlowcircuitMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNoiseMap       < Channel( RGBA, Box( NoiseMap ),       Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		const float3 tint = float3( 2.0, 5.0, 5.0 );

		float3 positionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		float heightMetres = positionWs.z / NS2_UNITS_PER_METRE;

		float3 normalWs = normalize( i.vNormalWs );
		float nDotV = saturate( dot( normalWs, normalize( -i.vPositionWithOffsetWs.xyz ) ) );
		float fresnel = pow( 1.0 - nDotV, 4.0 );

		float3 scan     = Ns2SrgbToLinear( Tex2DS( g_tScanMap, g_sWrap, heightMetres * float2( 2.0, 0.25 ) + float2( g_flTime, -g_flTime ) ).rgb );
		float3 circuit1 = Ns2SrgbToLinear( Tex2DS( g_tGlowcircuitMap, g_sWrap, uv * 12.0 ).rgb );
		float3 circuit2 = Ns2SrgbToLinear( Tex2DS( g_tCircuitMap, g_sWrap, uv * float2( 5.0, 4.0 ) ).rgb );
		float3 noise    = Ns2SrgbToLinear( Tex2DS( g_tNoiseMap, g_sWrap, uv * 0.4 + float2( 0.0, g_flTime * 0.52 ) ).rgb );

		m.Albedo = 0.0;
		m.Emission = float3( 0.0, 0.03, 0.7 )
			+ ( circuit2 * scan + scan * 0.05 ) * tint
			+ ( circuit1 * fresnel * noise ) * 2.0;

		m.Opacity = 1.0;
		m.Roughness = 1.0;
		m.Metalness = 0.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = normalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
