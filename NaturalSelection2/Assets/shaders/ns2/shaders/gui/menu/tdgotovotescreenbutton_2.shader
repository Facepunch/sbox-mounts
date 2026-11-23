HEADER
{
	Description = "NS2 tdgotovotescreenbutton_2";
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
	float2 g_vIndexSize < Default2( 3, 7 ); >;
	float2 g_vLeftTextureIndex < Default2( 0, 0 ); >;
	float2 g_vMiddleTextureIndex < Default2( 0, 0 ); >;
	float g_flGradientDiv < Default1( 0.5 ); >;
	float g_flSlope < Default1( 1 ); >;
	float g_flGradientBorderThickness < Default1( 0.007 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		float m = g_flSlope;
		float y = texCoord.y;
		float b = 0.5;

		float lineXOffset = ( y - b ) / m;
		float line1X = g_flGradientDiv + lineXOffset;

		float line1Distance = abs( texCoord.x - line1X );

		float2 atlasDiv = 1 / g_vIndexSize;
		float2 atlasOffset = texCoord * atlasDiv;

		float2 centerOffset = float2( atlasDiv.x * ( ( 1 - g_flGradientDiv ) / 2 ), 0 );
		float4 resultColor;
		if ( texCoord.x < line1X )
		{
			resultColor = g_tBaseTexture.Sample( g_sSparkWrap, atlasDiv * g_vLeftTextureIndex + atlasOffset + centerOffset );
		}
		else
		{
			resultColor = g_tBaseTexture.Sample( g_sSparkWrap, atlasDiv * g_vMiddleTextureIndex + atlasOffset - centerOffset );
		}

		resultColor.a = saturate( line1Distance / g_flGradientBorderThickness );

		resultColor = resultColor * color;
	}

	#include "ns2/gui_output.hlsl"
}
