HEADER
{
	Description = "NS2 weaponview_axe_emissive";
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
	float g_flHighlight < Default1( 0.0 ); >;
	float g_flTextureIndex < Default1( 0 ); >;
	float g_flNumRows < Default1( 1 ); >;
	float g_flNumColumns < Default1( 2 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		float highlightId = g_flHighlight;
		float2 uv = i.vTextureCoords.xy;
		float row = floor( g_flTextureIndex / g_flNumColumns );
		float col = g_flTextureIndex - row * g_flNumColumns;
		uv.x = ( col + uv.x ) / g_flNumColumns;
		uv.y = ( row + uv.y ) / g_flNumRows;
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );
		sparkAlbedo = Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb;
		sparkNormal = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy ) );
		sparkSpecular = specularGloss.rgb;
		sparkGloss = specularGloss.a;
		emission = SampleSrgb( g_tEmissiveMap, g_sWrap, uv ).rgb;
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
