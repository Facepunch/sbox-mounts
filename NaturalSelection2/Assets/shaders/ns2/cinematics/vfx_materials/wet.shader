HEADER
{
	Description = "NS2 wet";
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

	CreateInputTexture2D( EmissiveMap1, Linear, 8, "None", "_emissiveMap1", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap1 < Channel( RGBA, Box( EmissiveMap1 ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( EmissiveMap2, Linear, 8, "None", "_emissiveMap2", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap2 < Channel( RGBA, Box( EmissiveMap2 ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( MaskMap, Linear, 8, "None", "_maskMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaskMap < Channel( RGBA, Box( MaskMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( RefractMap, Linear, 8, "None", "_refractMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tRefractMap < Channel( RGBA, Box( RefractMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float3 g_vTint < Default3( 0, 0, 0 ); >;
	float4 g_vEm1Coord < Default4( 0, 0, 0, 0 ); >;
	float4 g_vEm2Coord < Default4( 0, 0, 0, 0 ); >;
	float4 g_vMaskCoord < Default4( 0, 0, 0, 0 ); >;
	float4 g_vNormCoord < Default4( 0, 0, 0, 0 ); >;
	float4 g_vNorm2Coord < Default4( 0, 0, 0, 0 ); >;
	float g_flNormIntensity < Default1( 0.10 ); >;
	float g_flDarkenGround < Default1( 0 ); >;
	float g_flScale < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float intensity = 1.0;
		float2 texCoord = i.vTextureCoords.xy;
		float smoothEdge = abs( i.vSparkPosition.y / g_flScale ) * 0.5;
		texCoord.x = clamp( texCoord.x + smoothEdge, -1, 1 );
		texCoord.y = clamp( texCoord.y - smoothEdge, -1, 1 );
		float distance = length( i.vSparkPosition );
		intensity *=
			clamp( 1 - ( distance / ( g_flScale * 0.5 ) ) * ( abs( i.vSparkPosition.y ) / ( g_flScale * 0.5 ) ), 0, 1 );
		float4 emiss1 = SampleSrgb(
			g_tEmissiveMap1, g_sWrap,
			i.vSparkPosition.xy * float2( g_vEm1Coord.r, g_vEm1Coord.g ) +
				float2( g_flTime * g_vEm1Coord.b, g_flTime * g_vEm1Coord.a ) +
				( Ns2DecodeNormal( SampleSrgb( g_tRefractMap, g_sWrap,
											   i.vSparkPosition.xy * float2( g_vNorm2Coord.r, g_vNorm2Coord.g ) +
												   float2( g_flTime * g_vNorm2Coord.b, g_flTime * g_vNorm2Coord.a ) ) )
					  .xy *
				  g_flNormIntensity ) );
		float4 emiss2 = SampleSrgb(
			g_tEmissiveMap2, g_sWrap,
			i.vSparkPosition.xy * float2( g_vEm2Coord.r, g_vEm2Coord.g ) +
				float2( g_flTime * g_vEm2Coord.b, g_flTime * g_vEm2Coord.a ) +
				( Ns2DecodeNormal( SampleSrgb( g_tRefractMap, g_sWrap,
											   i.vSparkPosition.xy * float2( g_vNormCoord.r, g_vNormCoord.g ) +
												   float2( g_flTime * g_vNormCoord.b, g_flTime * g_vNormCoord.a ) ) )
					  .xy *
				  g_flNormIntensity ) );
		float4 mask1 = SampleSrgb( g_tMaskMap, g_sWrap,
								   i.vTextureCoords.xy * float2( g_vMaskCoord.r, g_vMaskCoord.g ) +
									   float2( g_flTime * g_vMaskCoord.b, g_flTime * g_vMaskCoord.a ) );
		emission = ( ( emiss1.rgb * emiss2.rgb ) * mask1.rgb * g_vTint - ( mask1.rgb * g_flDarkenGround ) ) * intensity;
		opacity = mask1.r * intensity;
		return float4( emission, opacity );
	}
}
