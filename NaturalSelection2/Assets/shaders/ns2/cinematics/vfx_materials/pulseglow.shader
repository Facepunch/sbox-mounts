HEADER
{
	Description = "NS2 pulseglow";
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

	float3 g_vGlowColor < Default3( 1, 1, 1 ); >;
	float g_flFrequency < Default1( 1 ); >;
	float g_flStartTime < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float angle_intensity = 1.0 - saturate( dot( SparkViewVector( normalize( i.vNormalWs ) ), float3( 0, 0, -1 ) ) );
		float intensity = 0.5 - ( cos( ( g_flTime - g_flStartTime ) * 3.14159265 * 2 * g_flFrequency ) * 0.5 );
		intensity = intensity * intensity;
		emission = g_vGlowColor * intensity * angle_intensity;
		return float4( emission, opacity );
	}
}
