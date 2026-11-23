HEADER
{
	Description = "NS2 flipbook";
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
	float g_flFramesPerSecond < Default1( 1 ); >;
	float g_flHorizontalFrames < Default1( 1 ); >;
	float g_flVerticalFrames < Default1( 1 ); >;
	float g_flNumFrames < Default1( 1 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		float frameNumber = floor( fmod( g_flTime * g_flFramesPerSecond, g_flNumFrames ) );
		float2 frames = float2( floor( g_flHorizontalFrames ), floor( g_flVerticalFrames ) );
		float2 inverseFrames = float2( 1, 1 ) / frames;

		float2 framePosition = float2( fmod( frameNumber, frames.x ), floor( frameNumber * inverseFrames.x ) );

		resultColor = g_tBaseTexture.Sample( g_sSparkWrap, ( texCoord + framePosition ) * inverseFrames );
	}

	#include "ns2/gui_output.hlsl"
}
