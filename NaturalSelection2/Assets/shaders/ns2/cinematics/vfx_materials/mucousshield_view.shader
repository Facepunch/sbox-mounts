HEADER
{
	Description = "NS2 mucousshield_view";
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

	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noiseMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flEnzymeTime < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 0, 3, 0 );
		float t = g_flTime - g_flEnzymeTime;
		float3 noise = Tex2DS( g_tNoiseMap, g_sWrap,
							   ( i.vTextureCoords.xy + float2( g_flTime * -.07, g_flTime * .1 ) ) * float2( 2, 2.5 ) )
						   .rgb;
		float3 noise2 = Tex2DS( g_tNoiseMap, g_sWrap,
								( i.vTextureCoords.xy + float2( g_flTime * .17, g_flTime * -.1 ) ) * float2( 4, 5.3 ) )
							.rgb;
		emission = ( noise2 * noise ) * tint;
		return float4( emission, opacity );
	}
}
