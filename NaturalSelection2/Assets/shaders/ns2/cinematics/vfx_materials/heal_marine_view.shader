HEADER
{
	Description = "NS2 heal_marine_view";
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
	CreateInputTexture2D( Particles, Linear, 8, "None", "_particles", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tParticles < Channel( RGBA, Box( Particles ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flTimeLastHealed < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 0.02, 0.02, .3 );
		const float animationDuration = 2;
		const float pulseAnimationDuration = 0.6;
		float t = g_flTime - g_flTimeLastHealed;
		float animFraction = 1 - clamp( t / animationDuration, 0, 1 );
		float pulseAnimFraction = 1 - clamp( t / pulseAnimationDuration, 0, 1 );
		float3 noise = Tex2DS( g_tParticles, g_sWrap, i.vTextureCoords.xy * 12 + float2( g_flTime * 1, 1.3 * g_flTime ) ).rgb;
		float3 circuit = Tex2DS( g_tNoiseMap, g_sWrap, i.vTextureCoords.xy * 0.8 + float2( 2, 2 ) ).rgb;
		emission =
			( circuit * tint + pow( circuit * noise, 2.3 ) * 5 * pulseAnimFraction * float3( 0.5, 0.8, 1 ) ) * animFraction;
		return float4( emission, opacity );
	}
}
