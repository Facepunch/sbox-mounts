HEADER
{
	Description = "NS2 bilebomb";
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
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_EmissiveMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flHighlight < Default1( 0.5 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		float highlightId = g_flHighlight;
		const float3 tint = float3( .4, .42, .12 );
		const float3 tint2 = float3( 50, 12, 1 );
		float3 bubbles = SampleSrgb( g_tEmissiveMap, g_sWrap, i.vTextureCoords.xy * 8 ).rgb;
		float3 Noisenormal = Ns2DecodeNormal(
			SampleSrgb( g_tNormalMap, g_sWrap, i.vTextureCoords.xy * 6 + float2( g_flTime * -.01, g_flTime * -.04 ) ) );
		float3 albedo1 =
			SampleSrgb( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy * 6 + float2( g_flTime * .02, g_flTime * -.02 ) ).rgb;
		float3 albedo2 = SampleSrgb( g_tAlbedoMap, g_sWrap,
									 i.vTextureCoords.xy * float2( 10, 9 ) + float2( g_flTime * .02, g_flTime * -.04 ) )
							 .rgb;
		float fresnel = pow( max( -SparkViewVector( normalize( i.vNormalWs ) ).z, 0 ), 10 );
		float3 noise =
			SampleSrgb( g_tAlbedoMap, g_sWrap, ( i.vTextureCoords.xy * 1 + float2( 0, g_flTime * -.03 ) ) * float2( 1, 0.5 ) )
				.rgb;
		float3 noise2 = SampleSrgb( g_tAlbedoMap, g_sWrap, ( i.vTextureCoords.xy * 5 + float2( 0, g_flTime * -.2 ) ) ).rgb;
		emission = bubbles * fresnel * noise2 * tint2;
		sparkAlbedo = ( albedo1 * noise + albedo2 ) * tint;
		opacity = ( ( albedo1 + albedo2 ) * noise * 3 ).r;
		sparkNormal = Noisenormal;
		sparkGloss = 100;
		clip( opacity - 0.5 );
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
