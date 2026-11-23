HEADER
{
	Description = "NS2 skulk_hologram";
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

	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissiveMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NormalMap, Linear, 8, "None", "_normalMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flOpacity < Default1( 1.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float minDistSq = 9.0f;
		const float maxDistSq = 16.0f;
		const float minDistOpacity = 0.005f;
		float viewDistOpacity = saturate(
			( dot( ( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs ) /
					 NS2_METRE ),
				   ( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs ) /
					 NS2_METRE ) ) -
			  minDistSq ) /
			( maxDistSq - minDistSq ) );
		viewDistOpacity = viewDistOpacity * ( 1.0 - minDistOpacity ) + minDistOpacity;
		float3 tsNormal = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy ) );
		float3 vsNormal = tsNormal.x * SparkViewVector( normalize( i.vTangentUWs ) ) +
						  tsNormal.y * SparkViewVector( normalize( i.vTangentVWs ) ) +
						  tsNormal.z * SparkViewVector( normalize( i.vNormalWs ) );
		float normalMult = saturate( abs( dot( normalize( vsNormal ), float3( 0, 0, 1 ) ) ) );
		normalMult = 1.0 - normalMult;
		normalMult = normalMult * normalMult * normalMult;
		normalMult = normalMult * 5.0 + 0.125f;
		float3 tex = Tex2DS( g_tEmissiveMap, g_sWrap, i.vTextureCoords.xy ).rgb;
		emission = tex * g_flOpacity * viewDistOpacity * normalMult;
		return float4( emission, opacity );
	}
}
