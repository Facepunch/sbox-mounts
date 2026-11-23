HEADER
{
	Description = "NS2 asteroid_belt";
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
	float g_flEmissiveStrength < Default1( 1 ); >;
	float g_flOpacityStrength < Default1( 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float4 noise = SampleSrgb( g_tNoiseMap, g_sWrap, i.vTextureCoords.xy );
		float gradient = pow( 1 - i.vTextureCoords.xy.r, 4 );
		float4 emissive = SampleSrgb( g_tEmissiveMap, g_sWrap, i.vTextureCoords.xy ) * g_flEmissiveStrength;
		emission = emissive.rgb * ( noise.rgb * 1.5f );
		opacity = saturate( gradient * emissive.a * 1.5f ) * g_flOpacityStrength;
		return float4( emission, opacity );
	}
}
