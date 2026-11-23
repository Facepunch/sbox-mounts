HEADER
{
	Description = "NS2 hallucination";
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

	CreateInputTexture2D( GreebMap, Linear, 8, "None", "_greebMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGreebMap < Channel( RGBA, Box( GreebMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( CircuitMap, Linear, 8, "None", "_circuitMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tCircuitMap < Channel( RGBA, Box( CircuitMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noiseMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 0.5, pow( normalize( i.vNormalWs.yzx ).y + 1, 2 ) * 0.05, 1 );
		emission = tint * clamp( normalize( i.vNormalWs.yzx ).y, 0, 1 ) * 3;
		return float4( emission, opacity );
	}
}
