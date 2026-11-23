//=============================================================================
// Port of NS2 ns2/shaders/ExoMinigunView.surface_shader
//
//     const float detailNoiseAmount = 0.5;
//
//     float depth      = (input.osPosition.zzz * 2 - 2.5) * 0.5 - 0.5;
//     float heatAmount = input.osPosition.x < 0 ? heatAmountright : heatAmountleft;
//
//     float detailNoise = tex2D(noiseMap, input.texCoord * 10).r * detailNoiseAmount
//                       + (1 - detailNoiseAmount);
//     float3 tint = tex2D(heatMap, float2(heatAmount + depth, 0)).rgb * heatAmount * detailNoise;
//
//     float3 mask   = tex2D(maskMap, input.texCoord).rgb;
//     float3 noise  = tex2D(noiseMap, input.texCoord * 7 + float2(time * .03, time * -.01)).rgb;
//     float3 noise2 = tex2D(noiseMap, input.texCoord * 5 + float2(0, time * .034)).rgb;
//     float fresnel = pow(1 - max(-input.vsNormal.z, 0.1), 3);
//
//     material.emissive = tex2D(emissiveMap, input.texCoord).rgb
//                       + (fresnel*5 + (noise * noise2 * .2) + .1) * tint * mask;
//     material.id = 1;
//
// The exo minigun's barrels glowing as they overheat. Model + a heat term:
//
//  - Each barrel cluster heats independently. osPosition.x picks which of
//    heatAmountleft / heatAmountright applies, so the two miniguns share one
//    material but glow separately.
//  - depth is derived from osPosition.z and added to heatAmount when looking up
//    the 1D heatMap gradient, so the glow ramps along the barrel length rather
//    than lighting it uniformly. heatMap is clamp-addressed, which is what
//    stops that offset wrapping back to cold at the ends.
//  - Two noise layers scrolling at different speeds and directions multiply
//    together for a slow flame flicker, and a fresnel term rimlights the
//    barrels so the heat reads at grazing angles.
//
// NS2 computes fresnel from vsNormal.z. Spark's view space is right-handed
// with the camera down -z, so -vsNormal.z is the normal's component toward the
// viewer, i.e. dot(N, V). We use that directly from world-space vectors, which
// avoids needing a view matrix and is exactly equivalent.
//
// osPosition is in NS2 metres; Model.cs imports at 40 units per metre, so the
// object-space position is divided back down before use.
//
// On colour space: NS2 marks noiseMap, maskMap and heatMap srgb="true", so
// Spark hardware-decodes all three; we match with Ns2SrgbToLinear. It does NOT
// mark emissiveMap srgb, which is almost certainly an oversight given every
// other NS2 shader does, so we decode it anyway rather than let a gamma value
// land unconverted in a linear emission buffer.
//=============================================================================

HEADER
{
	Description = "NS2 Exo Minigun View";
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
	float4 vTintColor : COLOR1;
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

		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;

		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	SamplerState g_sWrap  < Filter( ANISO ); AddressU( WRAP );  AddressV( WRAP );  >;
	SamplerState g_sClamp < Filter( ANISO ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	CreateInputTexture2D( AlbedoMap,   Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap, Linear, 8, "None",             "_spec",     ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NoiseMap,    Linear, 8, "None",             "_noise",    ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( MaskMap,     Linear, 8, "None",             "_mask",     ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( HeatMap,     Linear, 8, "None",             "_heat",     ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNoiseMap    < Channel( RGBA, Box( NoiseMap ),    Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tMaskMap     < Channel( RGBA, Box( MaskMap ),     Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tHeatMap     < Channel( RGBA, Box( HeatMap ),     Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flHeatAmountleft  < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flHeatAmountright < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;

	static const float NS2_UNITS_PER_METRE = 40.0;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		const float detailNoiseAmount = 0.5;

		float2 uv = i.vTextureCoords.xy;
		float3 osPosition = i.vPositionOs / NS2_UNITS_PER_METRE;

		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb;
		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );

		float depth = ( osPosition.z * 2.0 - 2.5 ) * 0.5 - 0.5;
		float heatAmount = osPosition.x < 0.0 ? g_flHeatAmountright : g_flHeatAmountleft;

		float detailNoise = Tex2DS( g_tNoiseMap, g_sWrap, uv * 10.0 ).r * detailNoiseAmount + ( 1.0 - detailNoiseAmount );
		float3 tint = Ns2SrgbToLinear( Tex2DS( g_tHeatMap, g_sClamp, float2( heatAmount + depth, 0.0 ) ).rgb ) * heatAmount * detailNoise;

		float3 mask = Ns2SrgbToLinear( Tex2DS( g_tMaskMap, g_sWrap, uv ).rgb );
		float3 noise = Ns2SrgbToLinear( Tex2DS( g_tNoiseMap, g_sWrap, uv * 7.0 + float2( g_flTime * 0.03, g_flTime * -0.01 ) ).rgb );
		float3 noise2 = Ns2SrgbToLinear( Tex2DS( g_tNoiseMap, g_sWrap, uv * 5.0 + float2( 0.0, g_flTime * 0.034 ) ).rgb );

		float nDotV = max( dot( normalize( i.vNormalWs ), -normalize( i.vPositionWithOffsetWs.xyz ) ), 0.1 );
		float fresnel = pow( 1.0 - nDotV, 3.0 );

		Ns2ApplySpecularGloss( m, albedo * i.vTintColor.rgb, specularGloss.rgb, specularGloss.a );

		m.Emission = Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, uv ).rgb )
			+ ( fresnel * 5.0 + ( noise * noise2 * 0.2 ) + 0.1 ) * tint * mask;

		m.Opacity = 1.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = TransformNormal( Ns2DecodeNormal( normal ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
