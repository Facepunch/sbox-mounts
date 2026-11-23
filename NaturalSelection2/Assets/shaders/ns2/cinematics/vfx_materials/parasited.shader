HEADER
{
	Description = "NS2 parasited";
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
	CreateInputTexture2D( RefractMap, Linear, 8, "None", "_refractMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tRefractMap < Channel( RGBA, Box( RefractMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flScaleFactor < Default1( 1.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		float4 bab1 =
			SampleSrgb( g_tAlbedoMap, g_sWrap,
						i.vTextureCoords.xy * 16 + float2( g_flTime * .15, g_flTime * 1.8 ) +
							( Ns2DecodeNormal( SampleSrgb( g_tRefractMap, g_sWrap,
														   i.vTextureCoords.xy * 14 + float2( g_flTime * 3, g_flTime * 0.1 ) ) )
								  .xy *
							  .15 ) );
		float4 bab2 =
			SampleSrgb( g_tAlbedoMap, g_sWrap,
						i.vTextureCoords.xy * 12 + float2( g_flTime * -.15, g_flTime * 1.5 ) +
							( Ns2DecodeNormal( SampleSrgb( g_tRefractMap, g_sWrap,
														   i.vTextureCoords.xy * 7 + float2( g_flTime * -5, g_flTime * 1 ) ) )
								  .xy *
							  .1 ) );
		float fresnel = pow( max( -SparkViewVector( normalize( i.vNormalWs ) ).z, 0 ), 1 );
		sparkAlbedo = bab1.rgb + bab2.rgb;
		opacity = ( bab1.a + bab2.a ) * fresnel;
		clip( opacity - 0.5 );
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
