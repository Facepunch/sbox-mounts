HEADER
{
	Description = "NS2 energy_flow_red";
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

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 255, 3, 1 );
		float4 tex3 = SampleSrgb( g_tCloudMap, g_sWrap, i.vTextureCoords.xy * 2 + float2( g_flTime * 1, g_flTime * 0.5 ) );
		float4 tex2 =
			SampleSrgb( g_tEmissiveMap, g_sWrap, i.vTextureCoords.xy * .3 + float2( g_flTime * -.2, g_flTime * -.5 ) );
		float4 tex =
			SampleSrgb( g_tEmissiveMap, g_sWrap,
						i.vTextureCoords.xy * 1.5 + float2( g_flTime * .5, g_flTime * -0.8 ) +
							( Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap,
													   i.vTextureCoords.xy * 3 + float2( g_flTime * -1, g_flTime * 2 ) ) )
								  .xy *
							  .1 ) );
		emission = ( tex.rgb + tex2.rgb ) * tint;
		opacity = tex.r + tex2.r;
		return float4( emission, opacity );
	}
}
