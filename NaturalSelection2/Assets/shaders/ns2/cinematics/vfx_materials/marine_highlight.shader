HEADER
{
	Description = "NS2 marine_highlight";
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

	CreateInputTexture2D( NormalMap, Linear, 8, "None", "_normalMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flDistance < Default1( 2 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 1, 0.2, 0 ) * 3;
		float edge = clamp( 6 - g_flDistance * 0.8, 1.5, 6 );
		float3 tsNormal = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy ) );
		float3 vsNormal = tsNormal.x * SparkViewVector( normalize( i.vTangentUWs ) ) +
						  tsNormal.y * SparkViewVector( normalize( i.vTangentVWs ) ) +
						  tsNormal.z * SparkViewVector( normalize( i.vNormalWs ) );
		vsNormal = normalize( vsNormal );
		float3 vsView = normalize(
			( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs ) /
			  NS2_METRE ) );
		float f = 1 - saturate( dot( vsView, -vsNormal ) );
		emission = tint * pow( f, edge );
		return float4( emission, opacity );
	}
}
