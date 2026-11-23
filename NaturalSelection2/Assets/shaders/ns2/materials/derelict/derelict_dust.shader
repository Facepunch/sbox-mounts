HEADER
{
	Description = "NS2 derelict_dust";
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
	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noiseMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flXScroll < Default1( 0 ); >;
	float g_flYScroll < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float4 tex = SampleSrgb( g_tEmissiveMap, g_sWrap, i.vTextureCoords.xy + g_flTime * float2( g_flXScroll, g_flYScroll ) );
		float3 noise = SampleSrgb( g_tNoiseMap, g_sWrap,
								   i.vTextureCoords.xy * float2( .2, .7 ) + float2( g_flTime * .5, g_flTime * -2.61 ) )
						   .rgb;
		emission = tex.rgb * i.vColor.rgb * noise;
		opacity = ( tex.a * i.vColor.a ) / 2;
		return float4( emission, opacity );
	}
}
