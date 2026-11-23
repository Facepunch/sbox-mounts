HEADER
{
	Description = "NS2 jetpack_thruster";
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
	RenderState( CullMode, NONE );

	CreateInputTexture2D( MaskMap, Linear, 8, "None", "_maskMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaskMap < Channel( RGBA, Box( MaskMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noiseMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 2, 12, 80 );
		float3 mask = SampleSrgb( g_tMaskMap, g_sWrap, i.vTextureCoords.xy ).rgb;
		float3 noise = SampleSrgb( g_tNoiseMap, g_sWrap,
								   i.vTextureCoords.xy * float2( .2, .7 ) + float2( g_flTime * .5, g_flTime * -2.61 ) )
						   .rgb;
		float3 noise2 = SampleSrgb( g_tNoiseMap, g_sWrap,
									i.vTextureCoords.xy * float2( 1.2, .37 ) + float2( g_flTime * -.41, g_flTime * -4.73 ) )
							.rgb;
		float3 noise3 = SampleSrgb( g_tNoiseMap, g_sWrap,
									i.vTextureCoords.xy * float2( 0.8, 0.2 ) + float2( g_flTime * -.1, g_flTime * -6.3 ) )
							.rgb;
		float fresnel = pow( max( -SparkViewVector( normalize( i.vNormalWs ) ).z, 0 ), 13 );
		emission = ( noise * noise2 ) * tint;
		opacity = ( ( noise + noise2 + noise3 ) * 2 * mask ).r;
		return float4( emission, opacity );
	}
}
