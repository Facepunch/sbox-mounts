HEADER
{
	Description = "NS2 descent_skybox_planet_ring";
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

	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissiveMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( CloudMap, Linear, 8, "None", "_cloudMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tCloudMap < Channel( RGBA, Box( CloudMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float4 tex = SampleSrgb( g_tEmissiveMap, g_sWrap, i.vTextureCoords.xy * float2( 8, 1 ) + float2( g_flTime * .002, 0 ) );
		float4 tex2 = SampleSrgb( g_tCloudMap, g_sWrap, i.vTextureCoords.xy * float2( 20, 15 ) + float2( g_flTime * .01, 0 ) );
		float4 tex3 = SampleSrgb( g_tCloudMap, g_sWrap, i.vTextureCoords.xy * float2( 40, 10 ) + float2( g_flTime * .01, 0 ) );
		emission = tex.rgb + ( tex3.rgb + tex2.rgb ) * tex.rgb * 10;
		opacity = tex.a;
		return float4( emission, opacity );
	}
}
