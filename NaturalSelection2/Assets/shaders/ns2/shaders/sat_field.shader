HEADER
{
	Description = "NS2 sat_field";
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

	CreateInputTexture2D( Glowpoints, Linear, 8, "None", "_glowpoints", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGlowpoints < Channel( RGBA, Box( Glowpoints ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noiseMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( MaskMap, Linear, 8, "None", "_maskMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaskMap < Channel( RGBA, Box( MaskMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float distance =
			( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs ) /
			  NS2_METRE )
				.z;
		const float max_dist = 10.0f;
		const float min_dist = 3.0f;
		float hide_opacity = saturate( ( max_dist - distance ) / ( max_dist - min_dist ) );
		hide_opacity = hide_opacity * hide_opacity;
		const float3 tint = float3( 55, 0, 0 );
		float3 mask = SampleSrgb( g_tMaskMap, g_sWrap, i.vTextureCoords.xy * -6 + float2( g_flTime * 0, g_flTime * 0.2 ) ).rgb;
		float3 ash = SampleSrgb( g_tGlowpoints, g_sWrap, i.vTextureCoords.xy * 2 ).rgb;
		float3 noise =
			SampleSrgb( g_tNoiseMap, g_sWrap, i.vTextureCoords.xy * .02 + float2( g_flTime * -0.04, g_flTime * -0.1 ) ).rgb;
		float3 noise2 = SampleSrgb( g_tGlowpoints, g_sWrap,
									i.vTextureCoords.xy * float2( 0.02, .5 ) + float2( g_flTime * -0.01, g_flTime * 0.05 ) )
							.rgb;
		float3 noise3 =
			SampleSrgb( g_tGlowpoints, g_sWrap, i.vTextureCoords.xy * float2( 1.2, 1.1 ) + float2( 0, g_flTime * .02 ) ).rgb;
		float fresnel = pow( max( -SparkViewVector( normalize( i.vNormalWs ) ).z, 0 ), 5 );
		emission = ( noise * ash * noise3 * 10 * noise2 + ( 0.0008 * mask ) ) * tint * hide_opacity;
		return float4( emission, opacity );
	}
}
