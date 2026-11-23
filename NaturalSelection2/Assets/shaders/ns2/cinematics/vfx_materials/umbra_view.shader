HEADER
{
	Description = "NS2 umbra_view";
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
	float g_flIntensity < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 1, 0.35, 0 );
		const float speed = 0.4;
		float3 noise =
			Tex2DS( g_tNoiseMap, g_sWrap, ( i.vTextureCoords.xy * 3 + float2( g_flTime * -0.8, g_flTime * 0.6 ) * speed ) )
				.rgb *
			1.2;
		float3 noise2 =
			Tex2DS( g_tNoiseMap, g_sWrap, ( i.vTextureCoords.xy * 4 + float2( g_flTime * 0.6, g_flTime * -0.5 ) * speed ) )
				.rgb *
			1.2;
		float fresnel = abs( SparkViewVector( normalize( i.vNormalWs ) ).y );
		emission = noise * noise2 * tint * g_flIntensity * fresnel;
		return float4( emission, opacity );
	}
}
