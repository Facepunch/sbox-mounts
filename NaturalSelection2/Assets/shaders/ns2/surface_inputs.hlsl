#include "ns2/ns2.hlsl"

SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
SamplerState g_sClamp < Filter( ANISO ); AddressU( CLAMP ); AddressV( CLAMP ); >;

float4 SampleSrgb( Texture2D tex, SamplerState samplerState, float2 uv )
{
	float4 color = Tex2DS( tex, samplerState, uv );
	return float4( SrgbGammaToLinear( color.rgb ), color.a );
}

float3 SparkViewVector( float3 worldVector )
{
	float3 right = cross( g_vCameraDirWs, g_vCameraUpDirWs );
	return float3( dot( worldVector, right ), dot( worldVector, g_vCameraUpDirWs ), dot( worldVector, g_vCameraDirWs ) );
}
