HEADER
{
	Description = "NS2 descent_skybox_planet_asteroids";
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

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		float3 tint = float3( 1, 1, 1 );
		float4 albed = Tex2DS( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy * float2( 45, 6 ) + float2( g_flTime * .01, 0 ) );
		float4 albed2 = Tex2DS( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy * float2( 28, 4 ) + float2( g_flTime * .01, 0 ) );
		float4 albed3 = Tex2DS( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy * float2( 268, 20 ) + float2( g_flTime * .2, 0 ) );
		sparkAlbedo = albed.g * albed2.g * tint;
		emission = tint * ( albed.g + albed2.g + albed3.g );
		opacity = albed.b + albed2.b;
		clip( opacity - 0.5 );
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
