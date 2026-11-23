HEADER
{
	Description = "NS2 snow_splat";
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
	float3 vSparkPosition : TEXCOORD14;
};
VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		i.vSparkPosition = v.vPositionOs.yzx / 40.0;
		return FinalizeVertex( i );
	}
}
PS
{
	#include "ns2/surface_lighting.hlsl"

	CreateInputTexture2D( AlbedoMap, Linear, 8, "None", "_albedoMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tAlbedoMap < Channel( RGBA, Box( AlbedoMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flIntensity < Default1( 1.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		float2 texCoord = i.vSparkPosition.xz;
		float3 map = Tex2DS( g_tAlbedoMap, g_sWrap, texCoord ).rgb;
		float amount = Tex2DS( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy ).a;
		sparkAlbedo = map;
		opacity = saturate( amount * 0.4 ) * g_flIntensity;
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
