HEADER
{
	Description = "NS2 storm_view";
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
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		i.vPositionWs.xyz += i.vNormalWs * ( 40.0 * 0.004 );
		i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );
		return FinalizeVertex( i );
	}
}
PS
{
	#include "ns2/emissive_pass.hlsl"
	RenderState( DstBlend, ONE );

	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noiseMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flStartTime < Default1( 0.0 ); >;
	float g_flIntensity < Default1( 1.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 0.04, -0.02, 0.6 ) * g_flIntensity;
		float t = g_flTime - g_flStartTime;
		float3 noise2 =
			Tex2DS( g_tNoiseMap, g_sWrap, ( i.vTextureCoords.xy + float2( t * -0.25, t * -0.25 ) ) * float2( 8, 4 ) ).rgb;
		const float frequency = 3.14159;
		float amount = max( pow( sin( t * frequency ), 2 ) + 0.6 * pow( sin( t * frequency + 0.56 ), 2 ), 0 );
		emission = (noise2)*tint * amount;
		return float4( emission, opacity );
	}
}
