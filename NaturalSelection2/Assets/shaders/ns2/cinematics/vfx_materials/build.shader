HEADER
{
	Description = "NS2 build";
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

	CreateInputTexture2D( ScanMap, Linear, 8, "None", "_scanMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tScanMap < Channel( RGBA, Box( ScanMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( CircuitMap, Linear, 8, "None", "_circuitMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tCircuitMap < Channel( RGBA, Box( CircuitMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( GlowcircuitMap, Linear, 8, "None", "_glowcircuitMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGlowcircuitMap < Channel( RGBA, Box( GlowcircuitMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noiseMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 2, 5, 5 );
		float3 scan = SampleSrgb( g_tScanMap, g_sWrap,
								  ( ( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz ).yzx / NS2_METRE ).y *
										  float2( 2, .25 ) +
									  float2( g_flTime * .5, g_flTime * -.5 ) )
						  .rgb;
		float3 circuit1 = SampleSrgb( g_tGlowcircuitMap, g_sWrap, i.vTextureCoords.xy * 12 ).rgb;
		float3 circuit2 = SampleSrgb( g_tCircuitMap, g_sWrap, i.vTextureCoords.xy * float2( 5, 4 ) ).rgb;
		float3 noise = SampleSrgb( g_tNoiseMap, g_sWrap, i.vTextureCoords.xy * .4 + float2( 0, g_flTime * .2 ) ).rgb;
		float fresnel = pow( 1 - max( -SparkViewVector( normalize( i.vNormalWs ) ).z, 0 ), 4 );
		emission = ( circuit2 * scan + scan * 0.05 ) * tint + ( circuit1 * fresnel * noise ) * 2;
		return float4( emission, opacity );
	}
}
