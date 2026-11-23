HEADER
{
	Description = "NS2 guialienobjectivedisplay";
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
	CreateInputTexture2D( WavyMask, Linear, 8, "None", "_wavyMask", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tWavyMask < Channel( RGBA, Box( WavyMask ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noiseMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( SmokeMask, Linear, 8, "None", "_smokeMask", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tSmokeMask < Channel( RGBA, Box( SmokeMask ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flCorrectionX < Default1( 0 ); >;
	float g_flCorrectionY < Default1( 0 ); >;
	CreateInputTexture2D( FadeMap, Linear, 8, "None", "_fadeMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tFadeMap < Channel( RGBA, Box( FadeMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flStartTime < Default1( 0 ); >;
	float g_flEndTime < Default1( 1 ); >;
	float g_flStartOpacity < Default1( 0 ); >;
	float g_flEndOpacity < Default1( 0 ); >;

	float wave( float x, float t, float u, float y )
	{
		u = u * u * u;
		const float a = 6 * 3.14159;
		float period = a * cos( cos( t / 2 ) );
		float value = 4 * a * x + cos( period * x ) * ( 0.5 * t + 1 ) - 1;

		return u * value + ( 1 - u ) * x;
	}

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		float fade_i = saturate( ( g_flTime - g_flStartTime ) / ( g_flEndTime - g_flStartTime ) );
		float fade_opacity = g_flStartOpacity * ( 1.0 - fade_i ) + g_flEndOpacity * fade_i;
		float fade_alphaMult =
			saturate( saturate( g_tFadeMap.Sample( g_sSparkWrap, texCoord ).r ) - 1.0 + ( 2 * fade_opacity ) );

		float smoke_t = 2.5 * g_flTime;
		const float4 smoke_tint = float4( 4.1, 2.2, 1.3, 1.1 );
		const float4 smoke_tintoffset = float4( 0.16, 0.12, 0.05, 1 );

		const float2 smoke_correction = float2( g_flCorrectionX, g_flCorrectionY );
		float2 smoke_scrollingTexCoord = texCoord * smoke_correction * float2( 0.5, 0.5 );
		smoke_scrollingTexCoord.x += ( smoke_t * 0.003 );
		smoke_scrollingTexCoord.y += ( smoke_t * 0.002 );
		float4 smoke_noiseMap = g_tNoiseMap.Sample( g_sSparkWrap, smoke_scrollingTexCoord );

		const float2 smoke_correction2 = float2( g_flCorrectionX + 0.6, g_flCorrectionY + 0.5 );
		float2 smoke_scrollingTexCoord2 = texCoord * smoke_correction2 * float2( 0.7, 0.7 );
		smoke_scrollingTexCoord2.x += ( smoke_t * -0.005 );
		smoke_scrollingTexCoord2.y += ( smoke_t * 0.005 );
		float4 smoke_noiseMap2 = g_tNoiseMap.Sample( g_sSparkWrap, smoke_scrollingTexCoord2 );

		const float2 smoke_correction3 = float2( g_flCorrectionX + 0.7, g_flCorrectionY + 0.7 );
		float2 smoke_scrollingTexCoord3 = texCoord * smoke_correction3 * float2( 0.6, 0.8 );
		smoke_scrollingTexCoord3.x += ( smoke_t * 0.01 );
		smoke_scrollingTexCoord3.y += ( smoke_t * 0.01 );
		float4 smoke_noiseMap3 = g_tNoiseMap.Sample( g_sSparkWrap, smoke_scrollingTexCoord3 );

		float4 smoke_maskMap = g_tSmokeMask.Sample( g_sSparkWrap, texCoord );
		float4 smoke_resultColor = ( smoke_noiseMap3 * smoke_tintoffset ) + ( smoke_noiseMap * smoke_noiseMap2 * smoke_tint );
		smoke_resultColor.a *= smoke_maskMap.r;
		smoke_resultColor.rgb *= smoke_resultColor.a;

		float wave_t = g_flTime * 2;
		float wave_amount = g_tWavyMask.Sample( g_sSparkWrap, texCoord ).r;
		float2 wave_resultCoord;
		wave_resultCoord.x =
			wave( texCoord.x, cos( wave_t ), wave_amount, texCoord.y ) / wave( 1.0, cos( wave_t ), wave_amount, texCoord.y );
		wave_resultCoord.y = wave( texCoord.y, 1.2 * sin( smoke_t ), wave_amount, texCoord.x ) /
							 wave( 1.0, 1.2 * sin( wave_t ), wave_amount, texCoord.x );
		float4 wave_resultColor = g_tBaseTexture.Sample( g_sSparkWrap, wave_resultCoord );

		float4 composite_result;
		composite_result.rgb = wave_resultColor.rgb + smoke_resultColor.rgb * ( 1.0 - wave_resultColor.a );
		composite_result.a = wave_resultColor.a * ( 1.0 - smoke_resultColor.a ) + smoke_resultColor.a;

		composite_result.rgb = saturate( composite_result.rgb / ( composite_result.a + 0.0001 ) );

		composite_result.a *= fade_alphaMult;

		resultColor = composite_result;
	}

	#include "ns2/gui_output.hlsl"
}
