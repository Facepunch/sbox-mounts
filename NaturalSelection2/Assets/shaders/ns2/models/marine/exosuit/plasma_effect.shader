HEADER
{
	Description = "NS2 plasma_effect";
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
	CreateInputTexture2D( CloudMap, Linear, 8, "None", "_cloudMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tCloudMap < Channel( RGBA, Box( CloudMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NormalMap, Linear, 8, "None", "_normalMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flElecAmount < Default1( 0.5 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 40, 180, 280 );
		float4 tex3 = SampleSrgb( g_tCloudMap, g_sWrap, i.vTextureCoords.xy + float2( g_flTime * -.1, g_flTime * 0.5 ) );
		float4 tex2 = SampleSrgb( g_tEmissiveMap, g_sWrap, i.vTextureCoords.xy + float2( g_flTime * .2, g_flTime * .24 ) );
		float4 tex =
			SampleSrgb( g_tEmissiveMap, g_sWrap,
						i.vTextureCoords.xy * 2.4 + float2( g_flTime * .25, g_flTime * -1.5 ) +
							( Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap,
													   i.vTextureCoords.xy * 2.5 + float2( g_flTime * -0.1, g_flTime * 0.1 ) ) )
								  .xy *
							  .05 ) );
		emission = tex.rgb * tex2.rgb * tint;
		opacity = tex.r * tex3.a * tex3.r * 5 * g_flElecAmount;
		return float4( emission, opacity );
	}
}
