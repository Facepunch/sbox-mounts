HEADER
{
	Description = "NS2 alien_ghost_building_guide";
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
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		i.vPositionWs.z -= 0.4;
		i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );
		return FinalizeVertex( i );
	}
}
PS
{
	#include "ns2/surface_lighting.hlsl"

	CreateInputTexture2D( SmokeMap, Linear, 8, "None", "_smokeMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tSmokeMap < Channel( RGBA, Box( SmokeMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float3 g_vPulseColor < Default3( 1.0, 0.7333, 0.3569 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		const float2 correction = float2( 1, 1 );
		const float pulseFreq = 1.0;
		const float pulseMin = 0.25;
		const float pi = 3.14159265359;
		float smoke_t = 10.0 * g_flTime;
		const float4 smoke_tint = float4( 4.1, 2.2, 1.3, 1.1 );
		const float4 smoke_tintoffset = float4( 0.16, 0.12, 0.05, 1 );
		float2 texCoord = normalize( ( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz -
														g_vCameraPositionWs ) /
									   NS2_METRE ) )
							  .xy *
						  8.0f;
		float2 smoke_scrollingTexCoord = texCoord * correction * float2( 0.5, 0.5 );
		smoke_scrollingTexCoord.x += ( smoke_t * 0.003 );
		smoke_scrollingTexCoord.y += ( smoke_t * 0.002 );
		float4 smoke_noiseMap = Tex2DS( g_tSmokeMap, g_sWrap, smoke_scrollingTexCoord );
		float2 smoke_scrollingTexCoord2 = texCoord * ( correction + float2( 0.6, 0.5 ) ) * float2( 0.7, 0.7 );
		smoke_scrollingTexCoord2.x += ( smoke_t * -0.005 );
		smoke_scrollingTexCoord2.y += ( smoke_t * 0.005 );
		float4 smoke_noiseMap2 = Tex2DS( g_tSmokeMap, g_sWrap, smoke_scrollingTexCoord2 );
		float2 smoke_scrollingTexCoord3 = texCoord * ( correction + float2( 0.7, 0.7 ) ) * float2( 0.6, 0.8 );
		smoke_scrollingTexCoord3.x += ( smoke_t * 0.01 );
		smoke_scrollingTexCoord3.y += ( smoke_t * 0.01 );
		float4 smoke_noiseMap3 = Tex2DS( g_tSmokeMap, g_sWrap, smoke_scrollingTexCoord3 );
		float4 smoke_resultColor = ( smoke_noiseMap3 * smoke_tintoffset ) + ( smoke_noiseMap * smoke_noiseMap2 * smoke_tint );
		float pulse = ( sin( g_flTime * pi * 2 * pulseFreq ) * 0.5 + 0.5 ) * ( 1.0 - pulseMin ) + pulseMin;
		float pulseIntensity = 1.0f - saturate( dot( SparkViewVector( normalize( i.vNormalWs ) ), float3( 0, 0, -1 ) ) );
		pulseIntensity = pulseIntensity * pulse;
		pulseIntensity = pulseIntensity * pulseIntensity;
		emission = pow( smoke_resultColor.rgb, 1.0 / pulse ) + ( g_vPulseColor * pulseIntensity );
		sparkAlbedo = smoke_resultColor.rgb;
		sparkSpecular = float3( 0, 0, 0 );
		opacity = 1.0f;
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
