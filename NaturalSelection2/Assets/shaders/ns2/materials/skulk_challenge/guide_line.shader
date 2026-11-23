HEADER
{
	Description = "NS2 guide_line";
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

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float scrollSpeed = 1.0f;
		float endFade = saturate( -i.vTextureCoords.xy.y );
		endFade = endFade * endFade;
		float distFade = saturate(
			length( ( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs ) /
					  NS2_METRE ) ) *
				0.5 -
			0.5 );
		distFade = distFade * distFade;
		const float normalCosFadeStart = 0.25f;
		float normalFade =
			abs( dot( normalize( ( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz -
													g_vCameraPositionWs ) /
								   NS2_METRE ) ),
					  SparkViewVector( normalize( i.vNormalWs ) ) ) );
		normalFade = saturate( normalFade / normalCosFadeStart );
		emission = SampleSrgb( g_tEmissiveMap, g_sWrap,
							   float2( i.vTextureCoords.xy.x, i.vTextureCoords.xy.y + g_flTime * scrollSpeed ) )
					   .rgb *
				   distFade * endFade * normalFade;
		return float4( emission, opacity );
	}
}
