HEADER
{
	Description = "NS2 exo_plating_view";
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
	float3 vSparkPosition : TEXCOORD14;
};
VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		i.vSparkPosition = v.vPositionOs.yzx / 40.0;
		i.vPositionWs.xyz += i.vNormalWs * ( 40.0 * 0.01 );
		i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );
		return FinalizeVertex( i );
	}
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
		const float3 tint = float3( 0.8, 2, 2 );
		float3 greeb = SampleSrgb( g_tGreebMap, g_sWrap, i.vTextureCoords.xy * 8 ).rgb;
		float3 circuit2 = SampleSrgb( g_tCircuitMap, g_sWrap, i.vTextureCoords.xy * float2( 10, 9 ) ).rgb;
		float fresnel = pow( max( -SparkViewVector( normalize( i.vNormalWs ) ).z, 0 ), 2 );
		float3 noise =
			Tex2DS( g_tNoiseMap, g_sWrap, ( i.vTextureCoords.xy + float2( 0, g_flTime * 1 ) ) * float2( 1, 0.5 ) ).rgb;
		float mask = 0;
		if ( i.vSparkPosition.z > 0.6 )
		{
			mask = 1;
		}
		else
		{
			mask = clamp( abs( i.vSparkPosition.y ) - 0.05 / 0.5, 0, 1 );
		}
		emission = ( ( greeb * noise + circuit2 ) * fresnel ) * tint * mask;
		return float4( emission, opacity );
	}
}
