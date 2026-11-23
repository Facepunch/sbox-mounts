HEADER
{
	Description = "NS2 view_oil";
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
	float g_flAttackTime < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		const float bloodDissolveSpeed = 0.8;
		const float startBloodOpacity = 0.4;
		const float2 albedoScale = float2( 2.5, 1.8 );
		const float2 albedoScrollSpeed = float2( 0, -0.1 );
		const float2 normalScale = float2( 1.5, 1.8 );
		const float2 normalScrollSpeed = float2( 0, -0.12 );
		const float opacityScale1 = 1.1;
		const float opacityScale2 = 1.8;
		float t = g_flTime - g_flAttackTime;
		float opacity1 = Tex2DS( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy * opacityScale1 + t * float2( 0.10, -0.10 ) ).a;
		float opacity2 = Tex2DS( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy * opacityScale2 + t * float2( 0.05, -0.12 ) ).a;
		opacity = opacity1 * opacity2;
		sparkAlbedo = Tex2DS( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy * albedoScale + t * albedoScrollSpeed ).rgb;
		opacity = ( opacity - t * bloodDissolveSpeed + startBloodOpacity );
		sparkNormal =
			Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy * normalScale + t * normalScrollSpeed ) );
		sparkSpecular = float3( 1, 1, 1 );
		sparkGloss = 100;
		clip( opacity - 0.5 );
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
