HEADER
{
	Description = "NS2 strobe_grad";
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

	RenderState( CullMode, NONE );

	CreateInputTexture2D( AlbedoMap, Linear, 8, "None", "_albedoMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tAlbedoMap < Channel( RGBA, Box( AlbedoMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissiveMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( ScrollingMask, Linear, 8, "None", "_scrollingMask", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tScrollingMask < Channel( RGBA, Box( ScrollingMask ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NormalMap, Linear, 8, "None", "_normalMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		float4 albedo = SampleSrgb( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy );
		float shininess = albedo.a * 20;
		float glossiness = 20;
		float3 mask = SampleSrgb( g_tScrollingMask, g_sWrap, i.vTextureCoords.xy ).rgb;
		float2 movement = i.vTextureCoords.xy + float2( 0, g_flTime * -0.75 );
		sparkAlbedo = albedo.rgb;
		emission = SampleSrgb( g_tEmissiveMap, g_sWrap, movement ).rgb * mask;
		sparkNormal = Ns2DecodeNormal( SampleSrgb( g_tNormalMap, g_sWrap, i.vTextureCoords.xy ) );
		sparkSpecular = albedo.rgb * shininess;
		sparkGloss = glossiness / 255.0;
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
