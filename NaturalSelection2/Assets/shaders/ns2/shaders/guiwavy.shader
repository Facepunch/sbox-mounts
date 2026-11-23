HEADER
{
	Description = "NS2 guiwavy";
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

	float wave( float x, float t, float u, float y )
	{
		u = u * u * u;
		float a = 6 * 3.14159;
		float period = a * cos( cos( t / 2 ) );
		float value = 4 * a * x + cos( period * x ) * ( 0.5 * t + 1 ) - 1;

		return u * value + ( 1 - u ) * x;
	}

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		float t = g_flTime * 2;
		float amount = g_tWavyMask.Sample( g_sSparkWrap, texCoord ).r;
		float2 coord = texCoord;
		float2 resultCoord;

		resultCoord.x = wave( coord.x, cos( t ), amount, coord.y ) / wave( 1.0, cos( t ), amount, coord.y );
		resultCoord.y = wave( coord.y, 1.2 * sin( t ), amount, coord.x ) / wave( 1.0, 1.2 * sin( t ), amount, coord.x );

		resultColor = g_tBaseTexture.Sample( g_sSparkWrap, resultCoord ) * color;
	}

	#include "ns2/gui_output.hlsl"
}
