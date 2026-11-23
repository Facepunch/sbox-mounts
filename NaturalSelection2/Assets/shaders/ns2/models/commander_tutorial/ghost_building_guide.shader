HEADER
{
	Description = "NS2 ghost_building_guide";
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
	float3 vSparkPosition : TEXCOORD14;
};
VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		i.vSparkPosition = v.vPositionOs.yzx / 40.0;
		return FinalizeVertex( i );
	}
}
PS
{
	#include "ns2/emissive_pass.hlsl"
	RenderState( DstBlend, ONE );

	CreateInputTexture2D( ScanLinesMap, Linear, 8, "None", "_scanLinesMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tScanLinesMap < Channel( RGBA, Box( ScanLinesMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float scanlinesBlackLevel = 0.25;
		const float scanlinesScaleFactor = 0.25;
		const float2 scanlinesSpeed = float2( 0.01, 0.025 );
		float outerGlow = saturate( length( i.vSparkPosition.xz ) - 0.5 );
		outerGlow = outerGlow * 0.5 + 0.5;
		float3 tintColor = float3( 0, 0.7, 0.1 ) * 0.25 * ( ( sin( g_flTime * 4 ) + 1.5 ) / 2.5 );
		float2 vsPosition = normalize( ( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz -
														  g_vCameraPositionWs ) /
										 NS2_METRE ) )
								.xy;
		float2 scanlinesCoords =
			float2( vsPosition.x + g_flTime * scanlinesSpeed.x, vsPosition.y + g_flTime * scanlinesSpeed.y ) *
			scanlinesScaleFactor;
		float2 scanlinesCoords2 = float2( vsPosition.x, vsPosition.y ) * scanlinesScaleFactor * 2;
		float scanlines1 = Tex2DS( g_tScanLinesMap, g_sWrap, scanlinesCoords ).r;
		float scanlines2 = Tex2DS( g_tScanLinesMap, g_sWrap, scanlinesCoords2 ).r;
		float3 scanlines = pow( scanlines1 * scanlines2, 0.5 ) * tintColor;
		emission = scanlines * outerGlow;
		return float4( emission, opacity );
	}
}
