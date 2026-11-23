//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/minigun_overheat.surface_shader
//
//     float t = heatingValue;
//     const float3 tint  = float3(2, 0, 0);
//     const float3 tint2 = float3(.1, 0, 0);
//
//     float3 mask   = tex2D( maskMap, input.texCoord ).rgb;
//     float3 noise  = tex2D( noiseMap, input.texCoord * 7 + float2( time * .03, time * -.01) ).rgb;
//     float3 noise2 = tex2D( noiseMap, input.texCoord * 5 + float2(0, time * .034) ).rgb;
//     float  fresnel = pow(1 - max(-input.vsNormal.z, 0.1), 3);
//
//     material.emissive = ( fresnel*10 + (noise * noise2 * .2) + .1 ) * tint * mask * t;
//
// The red heat glow that builds on the exosuit minigun barrels.
//
// input.vsNormal.z: in Spark's view space a camera-facing normal gives
// -vsNormal.z == 1, so `max(-vsNormal.z, 0.1)` is N dot V clamped to a floor of
// 0.1. Written here as an explicit world-space N dot V, which does not depend
// on s&box's view basis matching Spark's. The 0.1 floor caps fresnel at
// pow(0.9, 3) = 0.729, so even a head-on barrel keeps a base glow of ~7.3.
//
// tint2 is declared and never used. Dropped.
//
// Both maps are srgb="true" and are decoded; tint is an authored constant and
// stays linear. heatingValue is driven by the game (0 cold, 1 glowing) and
// defaults to 0, so an unset material renders black.
//=============================================================================

HEADER
{
	Description = "NS2 Minigun Overheat";
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

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_color", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( MaskMap,  Linear, 8, "None", "_mask",  ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tMaskMap  < Channel( RGBA, Box( MaskMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flHeatingValue < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		const float3 tint = float3( 2.0, 0.0, 0.0 );

		float3 normalWs = normalize( i.vNormalWs );
		float nDotV = max( dot( normalWs, normalize( -i.vPositionWithOffsetWs.xyz ) ), 0.1 );
		float fresnel = pow( 1.0 - nDotV, 3.0 );

		float3 mask   = Ns2SrgbToLinear( Tex2DS( g_tMaskMap, g_sWrap, uv ).rgb );
		float3 noise  = Ns2SrgbToLinear( Tex2DS( g_tNoiseMap, g_sWrap, uv * 7.0 + float2( g_flTime * 0.03, g_flTime * -0.01 ) ).rgb );
		float3 noise2 = Ns2SrgbToLinear( Tex2DS( g_tNoiseMap, g_sWrap, uv * 5.0 + float2( 0.0, g_flTime * 0.034 ) ).rgb );

		m.Albedo = 0.0;
		m.Emission = ( fresnel * 10.0 + ( noise * noise2 * 0.2 ) + 0.1 ) * tint * mask * g_flHeatingValue;

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
