HEADER
{
	Description = "NS2 ambusher";
}
FEATURES
{
	#include "common/features.hlsl"
}
MODES
{
	Forward();
}
COMMON
{
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
	#include "ns2/emissive_pass.hlsl"
	RenderState( DstBlend, ONE );

	float3 g_vColor < Default3( 1, 1, 1 ); >;
	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noiseMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NormalMap, Linear, 8, "None", "_normalMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flFadeInTime < Default1( 1 ); >;
	float g_flFadeOutTime < Default1( 1 ); >;
	float g_flFadeInInfluence < Default1( 1 ); >;
	float g_flFadeOutInfluence < Default1( 1 ); >;
	float g_flSpeed < Default1( 1 ); >;
	float g_flNoiseScale < Default1( 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float3 tsNormal = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy ) );
		float3 vsNormal = tsNormal.x * SparkViewVector( normalize( i.vTangentUWs ) ) +
						  tsNormal.y * SparkViewVector( normalize( i.vTangentVWs ) ) +
						  tsNormal.z * SparkViewVector( normalize( i.vNormalWs ) );
		vsNormal = normalize( vsNormal );
		float rimIntensity = ( 1.0 - saturate( dot( vsNormal, float3( 0, 0, -1 ) ) ) ) * 0.75 + 0.25;
		float fadeInMult =
			( 1.0 - saturate( g_flFadeInTime - g_flTime ) ) * g_flFadeInInfluence + ( 1.0 - g_flFadeInInfluence );
		float fadeOutMult = saturate( g_flFadeOutTime - g_flTime ) * g_flFadeOutInfluence + ( 1.0 - g_flFadeOutInfluence );
		float fadeMult = fadeInMult * fadeOutMult;
		float gammaCorrection = 1.0 / ( saturate( fadeMult ) * 0.9999 + 0.0001 );
		float2 offset = g_flTime * float2( 1, 1 ) * g_flSpeed;
		float texIntensity = pow(
			saturate( SampleSrgb( g_tNoiseMap, g_sWrap, i.vTextureCoords.xy * g_flNoiseScale + offset ).r ), gammaCorrection );
		float distance = sqrt(
			( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs ) /
			  NS2_METRE )
					.x *
				( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs ) /
				  NS2_METRE )
					.x +
			( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs ) /
			  NS2_METRE )
					.y *
				( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs ) /
				  NS2_METRE )
					.y +
			( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs ) /
			  NS2_METRE )
					.z *
				( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs ) /
				  NS2_METRE )
					.z );
		float distIntensity = saturate( ( distance - 3.0 ) / 2.0 );
		distIntensity = distIntensity * distIntensity;
		float intensity = texIntensity * rimIntensity * distIntensity;
		emission = g_vColor * intensity;
		return float4( emission, opacity );
	}
}
