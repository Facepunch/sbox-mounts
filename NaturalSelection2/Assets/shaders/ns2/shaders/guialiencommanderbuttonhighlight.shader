HEADER
{
	Description = "NS2 guialiencommanderbuttonhighlight";
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
	CreateInputTexture2D( SmokeTexture, Linear, 8, "None", "_smokeTexture", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tSmokeTexture < Channel( RGBA, Box( SmokeTexture ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flFadeInTime < Default1( 0 ); >;
	float g_flFadeOutTime < Default1( 0 ); >;
	float g_flFadeOutWeight < Default1( 0 ); >;
	float g_flTimeOffset < Default1( 0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		const float pi = 3.14159265359f;
		const float deg2pi = pi / 180.0;
		const float fadeInDuration = 0.25f;
		const float fadeOutDuration = 1.0f;

		const float smokeMinAlpha = 144.0f / 255.0f;
		const float scaleAt0 = 1.25f;
		const float scaleAt1 = 0.75f;
		const float smokePeriod = 1.0f;
		const float steps = 3;
		const float angleStep = 63.0f * deg2pi;
		const float2 offsetStep = float2( 0.37f, 0.87f );

		const float powAt0 = 10.0f;
		const float powAt1 = 1.0f;

		const float pulseBase = 1.0f;
		const float pulseStrength = 2.0f;
		const float pulseFreq = 1.0f;

		float time2 = ( g_flTime + g_flTimeOffset );

		float fadeIn = ( saturate( ( g_flTime - g_flFadeInTime ) / fadeInDuration ) );
		fadeIn = fadeIn * fadeIn;
		float fadeOut =
			( g_flFadeOutWeight * ( saturate( ( g_flFadeOutTime - g_flTime ) / fadeOutDuration ) ) ) - g_flFadeOutWeight + 1;
		fadeOut = ( 2 * fadeOut ) - ( fadeOut * fadeOut );
		float fade = fadeIn * fadeOut;

		float pulse = ( sin( time2 * pi * 2 * pulseFreq ) * 0.5f + 0.5f ) * pulseBase + pulseStrength;

		float4 smokeAvg = float4( 0, 0, 0, 0 );
		float totalWeight = 0.0;
		for ( int i = 0; i < steps; ++i )
		{
			float smokeTime = ( time2 / smokePeriod ) + ( 13 * i ) + ( i / steps );
			float timeCycled = smokeTime - floor( smokeTime );
			float opacity = -abs( timeCycled * 2 - 1 ) + 1;
			float invScaleFactor = 1.0f / ( ( scaleAt1 - scaleAt0 ) * timeCycled + scaleAt0 );
			float angle = floor( smokeTime ) * angleStep;
			float xOffset = floor( smokeTime ) * offsetStep.x;
			float yOffset = floor( smokeTime ) * offsetStep.y;

			float s = 0.0f;
			float c = 0.0f;
			sincos( angle, s, c );

			float newXCoord = ( c * ( invScaleFactor * ( texCoord.x - 0.5f ) ) ) -
							  ( s * ( invScaleFactor * ( texCoord.y - 0.5f ) ) ) + 0.5f + xOffset;
			float newYCoord = ( s * ( invScaleFactor * ( texCoord.x - 0.5f ) ) ) +
							  ( c * ( invScaleFactor * ( texCoord.y - 0.5f ) ) ) + 0.5f + yOffset;

			smokeAvg = smokeAvg + g_tSmokeTexture.Sample( g_sSparkWrap, float2( newXCoord, newYCoord ) ) * opacity;
			totalWeight = totalWeight + opacity;
		}
		smokeAvg = smokeAvg / totalWeight;

		float blurMap = g_tBaseTexture.Sample( g_sSparkWrap, texCoord ).r;
		float scatterAmount = ( smokeAvg.a - smokeMinAlpha ) / ( 1.0f - smokeMinAlpha );
		float invGamma = ( powAt1 - powAt0 ) * scatterAmount + powAt0;
		float scatteredValue = pow( blurMap, invGamma );
		float4 factor = float4( scatteredValue, scatteredValue, scatteredValue, 1.0 );
		smokeAvg.a = 1.0f;

		float4 comp = factor * smokeAvg;
		comp = 3 * comp * comp - 2 * comp * comp * comp;

		resultColor = color * comp * fade * pulse;
	}

	#include "ns2/gui_output.hlsl"
}
