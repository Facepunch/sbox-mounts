HEADER
{
	Description = "NS2 undercity_fakeatmospherics";
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

	float g_flDecals < Default1( 0.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float vup = 1 - i.vTextureCoords.xy.g;
		float vdown = i.vTextureCoords.xy.g;
		float gradient = saturate( vdown * vdown * vdown - 0.02 ) * saturate( vup * vup * vup );
		emission = gradient.rrr * 4;
		return float4( emission, opacity );
	}
}
