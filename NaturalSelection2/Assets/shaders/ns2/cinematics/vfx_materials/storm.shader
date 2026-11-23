HEADER
{
	Description = "NS2 storm";
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
	float g_flOffset < Default1( 0.0 ); >;
	float g_flStartTime < Default1( 0.0 ); >;
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
		float phase = (g_flTime - g_flStartTime) * 4.4;
		float height = pow( 1 + sin( phase ), 2 ) * ( 1 + g_flOffset );
		i.vPositionWs.xyz += i.vNormalWs * ( 0.4 * ( height + 1 ) );
		i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );
		return FinalizeVertex( i );
	}
}
PS
{
	#include "ns2/surface_lighting.hlsl"

	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noiseMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flIntensity < Default1( 1.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		const float3 tint = float3( 1.0, 0.6, 0.2 ) * g_flIntensity;
		float t_storm = g_flTime - g_flStartTime;
		float height = pow( 1 + sin( t_storm * 4.4 ), 2 ) * ( 1 + g_flOffset );
		float3 noise2 = Tex2DS( g_tNoiseMap, g_sWrap,
								( i.vTextureCoords.xy + float2( t_storm * -0.25, t_storm * -0.25 ) ) * float2( 3.2, 2 ) )
							.rgb;
		const float frequency = 3.14159 * 1.4;
		float amount = max( pow( sin( t_storm * frequency ), 2 ) + 0.6 * pow( sin( t_storm * frequency + 0.28 ), 2 ), 0 );
		emission = (noise2)*tint * amount + float3( -0.4, -0.5, 0.05 ) * g_flIntensity;
		sparkAlbedo = (noise2)*tint * amount;
		opacity = noise2.r * amount;
		clip( opacity - 0.5 );
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
