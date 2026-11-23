HEADER
{
	Description = "NS2 health_indicator";
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
		const float3 tint = float3( 1, 0, 0 );
		float3 scan = SampleSrgb( g_tScanMap, g_sWrap,
								  ( ( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz ).yzx / NS2_METRE ).y *
										  float2( 3, .6 ) +
									  float2( g_flTime, g_flTime * -1 ) )
						  .rgb;
		emission = tint * clamp( cos( g_flTime * 4 ), 0.01, 1 ) + scan * tint * 10;
		return float4( emission, opacity );
	}
}
