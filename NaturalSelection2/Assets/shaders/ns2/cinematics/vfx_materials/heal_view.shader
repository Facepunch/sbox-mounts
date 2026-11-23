HEADER
{
	Description = "NS2 heal_view";
}
FEATURES
{
	#include "common/features.hlsl"
}
MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}
COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
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
	#include "ns2/surface_lighting.hlsl"

	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noiseMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flHealTime < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		const float3 tint = float3( .2, .4, .05 );
		float t = g_flTime - g_flHealTime;
		float3 noise = Tex2DS( g_tNoiseMap, g_sWrap, ( i.vTextureCoords.xy ) * float2( 20, 20.5 ) ).rgb;
		float3 noise2 = Tex2DS( g_tNoiseMap, g_sWrap,
								( i.vTextureCoords.xy + float2( g_flTime * .004, g_flTime * -.004 ) ) * float2( 4, 5.3 ) )
							.rgb;
		emission = ( noise2 + noise ) * tint;
		sparkAlbedo = ( noise2 + noise ) * tint;
		opacity = ( noise2.r * noise.r ) * 20 * t;
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
