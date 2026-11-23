//=============================================================================
// Port of NS2 ns2/seasonal/halloween2014/models/pumpkin/evilpumpkin.surface_shader
//
//     float3 glowmask = tex2D(emissiveMap, input.texCoord).rgb;
//     float3 lightng  = tex2D(elecMap, input.texCoord * 3.67 + float2( time * 2, time * -.05) ).rgb;
//     float3 lightng2 = tex2D(elecMap, input.texCoord * 5    + float2( time * 1.37, time * .07) ).rgb;
//     float flicker   = tex2D(flickerMap, float2(time * cos(flickerSpeed), 0)).r;
//
//     material.emissive = (( lightng + lightng2) * 6) * glowmask * input.color.a * flicker;
//
// Halloween jack-o'-lantern. This is docking_generator_01 with a flicker term
// bolted on, so the two scrolling elecMap layers are the same trick: one
// texture sampled at two scales and speeds so it beats against itself and
// never visibly loops. glowmask confines the result to the carved face.
//
// The flicker itself is a 1D lookup table. The texture is sampled along a
// single row at time * cos(flickerSpeed), so the artist paints a candle
// flicker curve into a strip of pixels instead of expressing it as maths.
// cos() of a speed is odd but faithful - it just means the material value is a
// phase, not a rate, and a flickerSpeed of 0 gives cos(0) = 1, full speed.
//=============================================================================

HEADER
{
	Description = "NS2 Evil Pumpkin";
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
	CreateInputTexture2D( EmissiveMap, Srgb,   8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( ElecMap,     Srgb,   8, "None",             "_elec",     ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( FlickerMap,  Linear, 8, "None",             "_flicker",  ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tElecMap     < Channel( RGBA, Box( ElecMap ),     Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tFlickerMap  < Channel( RGBA, Box( FlickerMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flFlickerSpeed < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 10 ); >;
	float g_flDecals < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb;
		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );

		float3 glowmask = Tex2DS( g_tEmissiveMap, g_sWrap, uv ).rgb;
		float3 lightng  = Tex2DS( g_tElecMap, g_sWrap, uv * 3.67 + float2( t * 2.0, t * -0.05 ) ).rgb;
		float3 lightng2 = Tex2DS( g_tElecMap, g_sWrap, uv * 5.0  + float2( t * 1.37, t * 0.07 ) ).rgb;

		// A 1D flicker curve painted into a strip of pixels, not a formula.
		float flicker = Tex2DS( g_tFlickerMap, g_sWrap, float2( t * cos( g_flFlickerSpeed ), 0.0 ) ).r;

		Ns2ApplySpecularGloss( m, albedo * i.vTintColor.rgb, specularGloss.rgb, specularGloss.a );

		m.Emission = ( ( lightng + lightng2 ) * 6.0 ) * glowmask * i.vColor.a * flicker;
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
