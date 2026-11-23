//=============================================================================
// Port of NS2 core/shaders/Level_emissive.surface_shader
//
//     float4 albedo = tex2D(albedoMap, input.texCoord);
//     float emissiveMult = emissiveMod;
//     float shininess  = albedo.a * 20;
//     float glossiness = 20;
//     if(emissiveMult == 0.0) { material.albedo = 0; material.specular = 0 * shininess; }
//     else                    { material.albedo = albedo.rgb; material.specular = albedo.rgb * shininess; }
//     material.tsNormal = DecodeNormal( tex2D(normalMap, input.texCoord) );
//     material.gloss    = glossiness / 255.0;
//     material.emissive = tex2D(emissiveMap, input.texCoord).rgb * emissiveMult;
//
// Level with an emissive map. The emissiveMod == 0 branch kills albedo AND
// specular, turning the surface pure black, so a light that has been switched
// off reads as an unlit hole rather than a lit-but-dark panel. Note that the
// else-branch specular is albedo.rgb * shininess exactly as in Level, and that
// the taken branch writes 0 * shininess which is simply 0.
//=============================================================================

HEADER
{
	Description = "NS2 Level Emissive";
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

	CreateInputTexture2D( AlbedoMap,   Linear, 8, "None",             "_color",    ",0/,0/0", Default4( 1.0, 1.0, 1.0, 0.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flEmissiveMod < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 100 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float4 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy );
		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy );
		float3 emissive = Tex2DS( g_tEmissiveMap, g_sWrap, i.vTextureCoords.xy ).rgb;

		float lit = g_flEmissiveMod == 0.0 ? 0.0 : 1.0;
		float3 baseColor = albedo.rgb * lit;
		float3 specular = saturate( baseColor * ( albedo.a * 20.0 ) );

		Ns2ApplySpecularGloss( m, baseColor, specular, 20.0 / 255.0 );

		m.Emission = Ns2SrgbToLinear( emissive ) * g_flEmissiveMod;
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
