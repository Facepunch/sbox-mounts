HEADER
{
	Description = "NS2 nsl_open_tournament_2019_animated";
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
	float g_flFrameCount < Default1( 30 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		const float pause = 5.0f;
		const float fps = 10.0f;

		const float totalTime = ( g_flFrameCount / fps ) + pause;
		const float frac = g_flTime / totalTime;
		const float cycle = totalTime * ( frac - floor( frac ) );
		const float progress = ( fps * max( 0.0f, cycle - pause ) ) / g_flFrameCount;
		const float currentFrame = min( floor( progress * g_flFrameCount ), g_flFrameCount - 1 );

		const float2 texCoordFactor = float2( 1.0f, 1.0f / g_flFrameCount );
		const float2 texCoordPer = float2( 0.0f, texCoordFactor.y );
		const float2 texCoordAnimated = texCoord * texCoordFactor + texCoordPer * currentFrame;

		resultColor = g_tBaseTexture.Sample( g_sSparkWrap, texCoordAnimated ) * color;
	}

	#include "ns2/gui_output.hlsl"
}
