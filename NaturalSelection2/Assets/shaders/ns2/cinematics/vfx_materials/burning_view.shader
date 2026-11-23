HEADER
{
	Description = "NS2 burning_view";
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
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		i.vPositionWs.xyz += i.vNormalWs * ( 40.0 * 0.001 );
		i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );
		return FinalizeVertex( i );
	}
}
PS
{
	#include "ns2/emissive_pass.hlsl"
	RenderState( DstBlend, ONE );

	CreateInputTexture2D( Glowpoints, Linear, 8, "None", "_glowpoints", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGlowpoints < Channel( RGBA, Box( Glowpoints ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noiseMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 100, 16, 2 );
		float3 ash = SampleSrgb( g_tGlowpoints, g_sWrap, i.vTextureCoords.xy * 12 ).rgb;
		float3 noise =
			SampleSrgb( g_tNoiseMap, g_sWrap, i.vTextureCoords.xy * 12 + float2( g_flTime * .35, g_flTime * 0.61 ) ).rgb;
		float3 noise2 = SampleSrgb( g_tNoiseMap, g_sWrap,
									i.vTextureCoords.xy * float2( 8.2, 9.7 ) + float2( g_flTime * -.41, g_flTime * .73 ) )
							.rgb;
		float3 noise3 = SampleSrgb( g_tNoiseMap, g_sWrap,
									i.vTextureCoords.xy * float2( 5.8, 6.1 ) + float2( g_flTime * -.1, g_flTime * .3 ) )
							.rgb;
		float fresnel = pow( max( -SparkViewVector( normalize( i.vNormalWs ) ).z, 0 ), 13 );
		emission = ( noise * noise2 ) * tint;
		opacity = ( ( ( noise + noise2 ) * noise3 + ash ) * ( 1 + fresnel * .5 ) ).r;
		return float4( emission, opacity );
	}
}
