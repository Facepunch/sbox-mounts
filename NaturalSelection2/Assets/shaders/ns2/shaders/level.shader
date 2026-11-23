//=============================================================================
// Port of NS2 core/shaders/Level.surface_shader
//
//     float4 albedo = tex2D(albedoMap, input.texCoord);
//     float shininess  = albedo.a * 20;
//     float glossiness = 20;
//     material.albedo    = albedo.rgb;
//     material.tsNormal  = DecodeNormal( tex2D(normalMap, input.texCoord) );
//     material.specular  = albedo.rgb * shininess;
//     material.gloss     = glossiness / 255.0;
//
// Spark's specular G-buffer is R8G8B8A8, so albedo.rgb * shininess saturates.
//=============================================================================

HEADER
{
	Description = "NS2 Level";
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

	CreateInputTexture2D( AlbedoMap, Linear, 8, "None",             "_color",  ",0/,0/0", Default4( 1.0, 1.0, 1.0, 0.0 ) );
	CreateInputTexture2D( NormalMap, Linear, 8, "NormalizeNormals", "_normal", ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );

	Texture2D g_tAlbedoMap < Channel( RGBA, Box( AlbedoMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float4 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy );
		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy );

		float3 specular = saturate( albedo.rgb * ( albedo.a * 20.0 ) );

		Ns2ApplySpecularGloss( m, albedo.rgb, specular, 20.0 / 255.0 );

		m.Opacity = 1.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = TransformNormal( Ns2DecodeNormal( normal ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = i.vTextureCoords.xy;

		return ShadingModelStandard::Shade( i, m );
	}
}
