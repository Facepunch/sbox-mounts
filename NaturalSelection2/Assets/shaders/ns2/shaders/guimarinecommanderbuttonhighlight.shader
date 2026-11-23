HEADER
{
	Description = "NS2 guimarinecommanderbuttonhighlight";
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
	CreateInputTexture2D( ScanlinesTexture, Linear, 8, "None", "_scanlinesTexture", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tScanlinesTexture < Channel( RGBA, Box( ScanlinesTexture ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flFadeInTime < Default1( 0 ); >;
	float g_flFadeOutTime < Default1( 0 ); >;
	float g_flFadeOutWeight < Default1( 0 ); >;
	float g_flTimeOffset < Default1( 0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		const float fadeInDuration = 0.25f;
		const float fadeOutDuration = 1.0f;
		const float glimmerRate = 1.0f;
		const float flickerIntensity = 0.25f;
		const float2 scanlineSpeed = float2( -0.00125, .001 );
		const float scanlineScale = 0.1f;
		const float intensity = 2.0f;
		const float pi = 3.141592654f;
		const float glimmerBoost = 1.0f;

		float time2 = g_flTime + g_flTimeOffset;

		float fadeIn = ( saturate( ( g_flTime - g_flFadeInTime ) / fadeInDuration ) );
		fadeIn = fadeIn * fadeIn;
		float fadeOut =
			( g_flFadeOutWeight * ( saturate( ( g_flFadeOutTime - g_flTime ) / fadeOutDuration ) ) ) - g_flFadeOutWeight + 1;
		fadeOut = ( 2 * fadeOut ) - ( fadeOut * fadeOut );
		float fade = fadeIn * fadeOut;

		float4 tex = g_tBaseTexture.Sample( g_sSparkWrap, texCoord );

		float frame = tex.r;

		float glimmer = ( sin( tex.g * 2 * pi + ( time2 * glimmerRate ) ) ) * 0.5 + 0.5;
		glimmer = glimmer * ( 1 + ( ( sin( time2 ) * 0.5 + 0.5 ) * glimmerBoost ) );

		float f1 = saturate( sin( time2 * 13 ) * 10 + 10.5 );
		float f2 = saturate( sin( time2 * 17 ) * 10 + 10.5 );
		float f3 = saturate( sin( time2 * 23 ) * 10 + 10.5 );
		float f4 = sin( time2 * 5 );
		float f5 = sin( time2 * 2 );
		float f6 = sin( time2 * 7 );
		float f7 = sin( time2 * 15 );
		float2 scanlineCoords = texCoord * scanlineScale + ( time2 * scanlineSpeed );
		float scanlines = g_tScanlinesTexture.Sample( g_sSparkWrap, scanlineCoords ).r *
						  ( f1 * f2 * f3 * f4 * f5 * f6 * f7 * flickerIntensity + ( 1.0 - flickerIntensity ) );

		float alpha = fade * frame * glimmer * scanlines;

		resultColor = float4( color.r, color.g, color.b, alpha * intensity );
	}

	#include "ns2/gui_output.hlsl"
}
