HEADER
{
	Description = "NS2 guismokesaturation";
}
MODES
{
	Default();
	Forward();
}
FEATURES
{
	#include "ui/features.hlsl"
}
COMMON
{
	#include "ui/common.hlsl"
}
VS
{
	#include "ui/vertex.hlsl"
}
PS
{
	#include "ns2/gui_pixel.hlsl"
	CreateInputTexture2D( BaseTexture, Linear, 8, "None", "_baseTexture", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tBaseTexture < Attribute( "Texture" ); Channel( RGBA, Box( BaseTexture ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( Noise, Linear, 8, "None", "_noise", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoise < Channel( RGBA, Box( Noise ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flCorrectionX < Default1( 0.5 ); >;
	float g_flCorrectionY < Default1( 0.5 ); >;
	float g_flSaturation < Default1( 1.0 ); >;
	float g_flTimeOffset < Default1( 1.0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		float t = 2.5 * ( g_flTime + g_flTimeOffset );
		const float4 tint = float4( 4.1, 2.2, 1.3, 1.1 );
		const float4 tintoffset = float4( .16, .12, .05, 1 );

		const float2 correction = float2( g_flCorrectionX, g_flCorrectionY );

		float2 scrollingTexCoord = texCoord * correction * float2( 0.5, 0.5 );
		scrollingTexCoord.x += ( ( t * 0.003 ) );
		scrollingTexCoord.y += ( t * 0.002 );
		float4 noiseMap = g_tNoise.Sample( g_sSparkWrap, scrollingTexCoord );

		const float2 correction2 = float2( g_flCorrectionX + 0.6, g_flCorrectionY + 0.5 );
		float2 scrollingTexCoord2 = texCoord * correction2 * float2( 0.7, 0.7 );
		scrollingTexCoord2.x += ( ( t * -0.005 ) );
		scrollingTexCoord2.y += ( t * 0.005 );
		float4 noiseMap2 = g_tNoise.Sample( g_sSparkWrap, scrollingTexCoord2 );

		const float2 correction3 = float2( g_flCorrectionX + 0.7, g_flCorrectionY + 0.7 );
		float2 scrollingTexCoord3 = texCoord * correction3 * float2( .6, .8 );
		scrollingTexCoord3.x += ( ( t * 0.01 ) );
		scrollingTexCoord3.y += ( t * 0.01 );
		float4 noiseMap3 = g_tNoise.Sample( g_sSparkWrap, scrollingTexCoord3 );

		float maskMap = g_tBaseTexture.Sample( g_sSparkWrap, texCoord ).r;
		float4 result = ( ( noiseMap3 * tintoffset ) + ( noiseMap * noiseMap2 * tint ) );
		result.a *= maskMap * color.a;

		float lum = result.r * 0.21f + result.g * 0.72f + result.b * 0.07f;
		result.rgb = ( result.rgb * g_flSaturation ) + ( lum * ( 1.0f - g_flSaturation ) );

		resultColor = result;
	}

	#include "ns2/gui_output.hlsl"
}
