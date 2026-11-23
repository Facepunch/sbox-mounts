HEADER
{
	Description = "NS2 exoview";
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
	#define CUSTOM_MATERIAL_INPUTS
	#include "common/shared.hlsl"
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
	#include "ns2/surface_vertex.hlsl"
}
PS
{
	#include "ns2/surface_lighting.hlsl"

	CreateInputTexture2D( AlbedoMap, Linear, 8, "None", "_albedoMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tAlbedoMap < Channel( RGBA, Box( AlbedoMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NormalMap, Linear, 8, "None", "_normalMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( SpecularMap, Linear, 8, "None", "_specularMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissiveMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( LightMap, Linear, 8, "None", "_lightMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tLightMap < Channel( RGBA, Box( LightMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flCloseAmount < Default1( 0.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, i.vTextureCoords.xy );
		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy ).rgb;
		float3 emissive = SampleSrgb( g_tEmissiveMap, g_sWrap, i.vTextureCoords.xy ).rgb;
		float3 light = SampleSrgb( g_tLightMap, g_sWrap, i.vTextureCoords.xy ).rgb;
		float3 specular =
			pow( saturate( dot( SparkViewVector( normalize( i.vNormalWs ) ), normalize( float3( 0, 1, -1 ) ) ) ), 100 ) *
			specularGloss.rgb;
		float closeFraction = pow( g_flCloseAmount, 4 );
		sparkAlbedo = albedo * ( 1 - closeFraction );
		sparkNormal = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy ) );
		sparkSpecular = specularGloss.rgb * ( 1 - closeFraction );
		sparkGloss = specularGloss.a;
		emission = emissive * g_flCloseAmount * 2 + ( light * albedo + specular ) * closeFraction;
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
