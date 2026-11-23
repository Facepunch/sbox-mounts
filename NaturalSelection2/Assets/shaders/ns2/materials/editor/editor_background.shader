HEADER
{
	Description = "NS2 editor_background";
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

	float3 g_vColor1 < Default3( 0, 0, 0 ); >;
	float3 g_vColor2 < Default3( 0, 0, 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float f = i.vTextureCoords.xy.y;
		emission = pow( lerp( g_vColor1, g_vColor2, f ), 2.2 );
		return float4( emission, opacity );
	}
}
