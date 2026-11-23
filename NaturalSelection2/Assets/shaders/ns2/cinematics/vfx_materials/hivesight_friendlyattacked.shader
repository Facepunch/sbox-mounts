HEADER
{
	Description = "NS2 hivesight_friendlyattacked";
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
	float g_flIntensity < Default1( 0.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float3 color = float3( 0, 0.5, 0.0 ) + float3( 0.5 * ( 1 + cos( g_flTime * 5 ) ), 0, 0 );
		color *= color * 0.2;
		float3 scan = SampleSrgb( g_tScanMap, g_sWrap,
								  ( ( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz ).yzx / NS2_METRE ).y *
										  float2( 2, .25 ) +
									  float2( g_flTime, g_flTime * -1 ) )
						  .rgb;
		float3 circuit1 = color * SampleSrgb( g_tGlowcircuitMap, g_sWrap, i.vTextureCoords.xy * 12 ).rgb;
		float3 circuit2 = color * SampleSrgb( g_tCircuitMap, g_sWrap, i.vTextureCoords.xy ).rgb;
		float3 noise = SampleSrgb( g_tNoiseMap, g_sWrap, i.vTextureCoords.xy * .4 + float2( 0, g_flTime * .52 ) ).rgb;
		float fresnel = pow( 1 - max( -SparkViewVector( normalize( i.vNormalWs ) ).z, 0 ), 4 );
		emission = g_flIntensity * ( color + ( circuit2 * scan + scan * 0.05 ) + ( circuit1 * fresnel * noise ) * 2 );
		return float4( emission, opacity );
	}
}
