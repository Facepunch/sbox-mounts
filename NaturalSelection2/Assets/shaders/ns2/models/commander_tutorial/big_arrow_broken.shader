HEADER
{
	Description = "NS2 big_arrow_broken";
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

	CreateInputTexture2D( GlowMap, Linear, 8, "None", "_glowMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGlowMap < Channel( RGBA, Box( GlowMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( ScanLinesMap, Linear, 8, "None", "_scanLinesMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tScanLinesMap < Channel( RGBA, Box( ScanLinesMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float3 g_vTintColor < Default3( 1, 1, 1 ); >;
	float g_flFade < Default1( 0.5 ); >;

	float flicker( float t, float frequency )
	{
		float phase = frequency * t;
		return saturate( 24 * abs( phase - floor( phase + 0.5 ) ) - 10 );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float scanlinesBlackLevel = 0.25;
		const float scanlinesScaleFactor = 0.25;
		const float2 scanlinesSpeed = float2( 0.01, 0.025 );
		float2 vsPosition = normalize( ( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz -
														  g_vCameraPositionWs ) /
										 NS2_METRE ) )
								.xy;
		float edgeGlowFact = Tex2DS( g_tGlowMap, g_sWrap, i.vTextureCoords.xy ).r;
		float scanlinesFact = edgeGlowFact * ( 1.0 - scanlinesBlackLevel ) + scanlinesBlackLevel;
		float2 scanlinesCoords =
			float2( vsPosition.x + g_flTime * scanlinesSpeed.x, vsPosition.y + g_flTime * scanlinesSpeed.y ) *
			scanlinesScaleFactor;
		float2 scanlinesCoords2 = float2( vsPosition.x, vsPosition.y ) * scanlinesScaleFactor * 2;
		float scanlines1 = Tex2DS( g_tScanLinesMap, g_sWrap, scanlinesCoords ).r;
		float scanlines2 = Tex2DS( g_tScanLinesMap, g_sWrap, scanlinesCoords2 ).r;
		float3 scanlines = pow( scanlines1 * scanlines2, 0.5 ) * g_vTintColor * scanlinesFact;
		float3 edgeGlow = g_vTintColor * edgeGlowFact;
		float opacityFactor = g_flFade * g_flFade;
		float fTime = g_flTime / 23;
		float activity = ( flicker( fTime, 7 ) + flicker( fTime, 13 ) + flicker( fTime, 17 ) + flicker( fTime, 23 ) ) * 0.25;
		float3 lit = opacityFactor * ( 1.0 - ( ( 1.0 - scanlines ) * ( 1.0 - edgeGlow ) ) );
		float notLitMono = lit.g * 0.5;
		float3 notLit = float3( notLitMono, notLitMono, notLitMono );
		emission = lit * activity + notLit * ( 1.0 - activity );
		return float4( emission, opacity );
	}
}
