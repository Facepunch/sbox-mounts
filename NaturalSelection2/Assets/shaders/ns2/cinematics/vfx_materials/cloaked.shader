HEADER
{
	Description = "NS2 cloaked";
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
	float g_flCloakAmount < Default1( 0.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 0.4, 0.7, 1.0 ) * 0.8;
		const float3 tint2 = float3( 0.4, 0.4, 1.0 ) * 0.1;
		float normal = pow( abs( normalize( i.vNormalWs.yzx ).y ), 2 );
		float3 noise =
			Tex2DS( g_tNoiseMap, g_sWrap, i.vTextureCoords.xy * 2 + float2( g_flTime * .25, g_flTime * 0.41 ) * 0.2 ).rgb;
		float3 noise2 =
			Tex2DS( g_tNoiseMap, g_sWrap, i.vTextureCoords.xy * 1.5 + float2( g_flTime * .41, g_flTime * 0.25 ) * 0.3 ).rgb;
		emission = ( normal * noise * tint + noise2 * tint2 ) * g_flCloakAmount;
		return float4( emission, opacity );
	}
}
