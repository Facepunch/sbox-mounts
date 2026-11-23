HEADER
{
	Description = "NS2 skulkview";
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

	CreateInputTexture2D( MaterialAlbedoMap, Linear, 8, "None", "_materialAlbedoMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaterialAlbedoMap < Channel( RGBA, Box( MaterialAlbedoMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( MaterialNormalMap, Linear, 8, "None", "_materialNormalMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaterialNormalMap < Channel( RGBA, Box( MaterialNormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( MaterialSpecularMap, Linear, 8, "None", "_materialSpecularMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaterialSpecularMap < Channel( RGBA, Box( MaterialSpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( BloodMap, Linear, 8, "None", "_bloodMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tBloodMap < Channel( RGBA, Box( BloodMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flAttackTime < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		float4 specularGloss = Tex2DS( g_tMaterialSpecularMap, g_sWrap, i.vTextureCoords.xy );
		const float speed = 0.5;
		const float albedoScrollSpeed = 0.1;
		const float opacityScrollSpeed1 = 0.1;
		const float opacityScrollSpeed2 = 0.5;
		const float maxBloodAmount = 0.75;
		float bloodAmount = saturate( 1 - ( g_flTime - g_flAttackTime ) * speed );
		float2 opacityTexCoord1 = i.vTextureCoords.xy * float2( 1.1, 0.5 ) - float2( 0, g_flTime * opacityScrollSpeed1 );
		float2 opacityTexCoord2 = i.vTextureCoords.xy * float2( 1.8, 1.4 ) - float2( 0, g_flTime * opacityScrollSpeed2 );
		float bloodOpacity =
			Tex2DS( g_tBloodMap, g_sWrap, opacityTexCoord1 ).a * Tex2DS( g_tBloodMap, g_sWrap, opacityTexCoord2 ).a;
		if ( bloodOpacity > 2 - bloodAmount - maxBloodAmount )
		{
			float2 t1 = i.vTextureCoords.xy * float2( 2.5, 1.8 ) - float2( 0, g_flTime * albedoScrollSpeed );
			sparkAlbedo = Tex2DS( g_tBloodMap, g_sWrap, t1 ).rgb;
		}
		else
		{
			sparkAlbedo = Tex2DS( g_tMaterialAlbedoMap, g_sWrap, i.vTextureCoords.xy ).rgb;
		}
		sparkSpecular = specularGloss.rgb;
		sparkNormal = Ns2DecodeNormal( Tex2DS( g_tMaterialNormalMap, g_sWrap, i.vTextureCoords.xy ) );
		sparkGloss = specularGloss.a;
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
