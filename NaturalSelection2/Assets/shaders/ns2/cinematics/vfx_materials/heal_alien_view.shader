HEADER
{
	Description = "NS2 heal_alien_view";
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
	float g_flTimeLastHealed < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( .2, .4, .05 );
		const float animationDuration = 2;
		const float pulseAnimationDuration = 0.7;
		float t = g_flTime - g_flTimeLastHealed;
		float animFraction = 1 - clamp( t / animationDuration, 0, 1 );
		float pulseAnimFraction = 1 - clamp( t / pulseAnimationDuration, 0, 1 );
		float3 noise = Tex2DS( g_tNoiseMap, g_sWrap, i.vTextureCoords.xy + float2( 0.3, -0.3 ) * g_flTime ).rgb;
		float3 veins = Tex2DS( g_tNoiseMap, g_sWrap, i.vTextureCoords.xy * 1.4 ).rgb;
		emission = ( veins * .35 + pow( veins * noise, 2 ) * 30 * pulseAnimFraction * float3( 1, 1, 0 ) ) * tint * animFraction;
		return float4( emission, opacity );
	}
}
