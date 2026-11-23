//=============================================================================
// Port of NS2 ns2/models/alien/babbler/babbler_ball.surface_shader
//
//     float4 specularGloss = tex2D(specularMap, input.texCoord);
//     material.albedo   = tex2D(albedoMap, input.texCoord).rgb;
//     material.tsNormal = DecodeNormal( tex2D(normalMap, input.texCoord) );
//     material.specular = specularGloss.rgb;
//     material.gloss    = specularGloss.a;
//     material.emissive = tex2D(emissiveMap, input.texCoord).rgb * (1.2 + cos(time * 5)) * 10;
//     material.id       = 1*highlightId; // To make it show up on alien vision
//
// Model_emissive with the emissive term pulsing between 0.2x and 2.2x and then
// scaled by 10, so the ball throbs hard. emissiveMap is srgb="true" so Spark
// hardware-decodes it; mounted DDS arrive UNORM, hence the explicit decode.
//
// material.id feeds Spark's alien vision pass, which has no s&box equivalent.
// The input is kept so materials that set it still bind, but it is inert.
//=============================================================================

HEADER
{
	Description = "NS2 Babbler Ball";
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
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;

		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;

		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap,   Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap, Linear, 8, "None",             "_spec",     ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flHiddenAmount < Default1( 0 ); >;
	float g_flHighlight < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float opacity = 1 - g_flHiddenAmount;
		clip( opacity - 0.5 );
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb;
		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );
		float3 emissive = Tex2DS( g_tEmissiveMap, g_sWrap, uv ).rgb;

		Ns2ApplySpecularGloss( m, albedo * i.vTintColor.rgb, specularGloss.rgb, specularGloss.a );

		m.Emission = Ns2SrgbToLinear( emissive ) * ( 1.2 + cos( g_flTime * 5.0 ) ) * 10.0 * opacity;

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
