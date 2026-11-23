HEADER
{
	Description = "NS2 boneshield";
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
		i.vPositionWs.xyz += i.vNormalWs * ( 40.0 * 0.05 );
		i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );
		return FinalizeVertex( i );
	}
}
PS
{
	#include "ns2/emissive_pass.hlsl"
	RenderState( DstBlend, ONE );

	CreateInputTexture2D( EmissiveMap1, Linear, 8, "None", "_emissiveMap1", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap1 < Channel( RGBA, Box( EmissiveMap1 ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( MaskMap, Linear, 8, "None", "_maskMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaskMap < Channel( RGBA, Box( MaskMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float3 g_vTint < Default3( 0, 0, 0 ); >;
	float2 g_vEm1Coord < Default2( 0, 0 ); >;
	float2 g_vEm2Coord < Default2( 0, 0 ); >;
	float2 g_vMaskCoord < Default2( 0, 0 ); >;
	float g_flEmissiveMod < Default1( 0 ); >;
	float g_flAddBlending < Default1( 0 ); >;
	float g_flMaskOpacity < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float emissiveMult = g_flEmissiveMod;
		float3 em = SampleSrgb( g_tEmissiveMap1, g_sWrap, i.vTextureCoords.xy * float2( g_vEm1Coord.r, g_vEm1Coord.g ) ).rgb;
		float3 em2 = SampleSrgb( g_tEmissiveMap1, g_sWrap, i.vTextureCoords.xy * float2( g_vEm2Coord.r, g_vEm2Coord.g ) ).rgb;
		float3 mask = SampleSrgb( g_tMaskMap, g_sWrap,
								  i.vTextureCoords.xy * g_flMaskOpacity * float2( g_vMaskCoord.r, g_vMaskCoord.g ) +
									  float2( g_flTime * -.2, g_flTime * .2 ) )
						  .rgb;
		float front = saturate( ceil( i.vSparkPosition.z - 0.001 ) );
		float gradient = saturate( pow( ( i.vSparkPosition.z - 0.001 ), 2 ) * 4 );
		float frontmask = front * gradient;
		emission = ( ( em.rgb + em2.rgb ) * frontmask ) * mask.rgb * g_vTint * emissiveMult;
		opacity = ( mask.rgb ).r;
		return float4( emission, opacity );
	}
}
