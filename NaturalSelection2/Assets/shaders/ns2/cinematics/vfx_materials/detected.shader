HEADER
{
	Description = "NS2 detected";
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
	float g_flTimeDetected < Default1( 1.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 2, 5, 5 );
		const float animationDuration = 2;
		float animationTime = g_flTime - g_flTimeDetected;
		float alpha = 1 - clamp( ( animationTime ) / animationDuration, 0, 1 );
		float3 scan = SampleSrgb( g_tScanMap, g_sWrap,
								  ( ( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz ).yzx / NS2_METRE ).y *
										  float2( 8, 8 ) +
									  float2( animationTime * 1, animationTime * -1 ) )
						  .rgb;
		emission = scan * alpha * 12;
		return float4( emission, opacity );
	}
}
