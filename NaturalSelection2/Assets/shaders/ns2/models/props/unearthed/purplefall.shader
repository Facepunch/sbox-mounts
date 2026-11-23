HEADER
{
	Description = "NS2 purplefall";
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
		i.vPositionWs.xyz += i.vNormalWs * ( 40.0 * 0.03 );
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
	float3 g_vTint < Default3( 0, 0, 0 ); >;
	float g_flIntensity < Default1( 0 ); >;
	float g_flTranslucency < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float3 ash = SampleSrgb( g_tGlowpoints, g_sWrap,
								 i.vTextureCoords.xy * float2( 0.8, .7 ) + float2( g_flTime * -.025, g_flTime * -.35 ) )
						 .rgb;
		float3 noise = SampleSrgb( g_tGlowpoints, g_sWrap,
								   i.vTextureCoords.xy * float2( 0.2, .5 ) + float2( g_flTime * .025, g_flTime * -0.25 ) )
						   .rgb;
		float3 noise2 = SampleSrgb( g_tNoiseMap, g_sWrap,
									i.vTextureCoords.xy * float2( 0.3, 1.3 ) + float2( g_flTime * -.01, g_flTime * -.5 ) )
							.rgb;
		float3 noise3 = SampleSrgb( g_tGlowpoints, g_sWrap,
									i.vTextureCoords.xy * float2( 0.6, 2.1 ) + float2( g_flTime * -.02, g_flTime * -.8 ) )
							.rgb;
		emission = ( noise * noise2 + ash + noise3 ) * ( g_vTint * g_flIntensity );
		opacity = ( ( ( noise + noise2 ) + noise3 + ash ) * 2.5 + g_flTranslucency ).r;
		return float4( emission, opacity );
	}
}
