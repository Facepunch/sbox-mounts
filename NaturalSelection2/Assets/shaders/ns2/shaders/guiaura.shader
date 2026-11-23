HEADER
{
	Description = "NS2 guiaura";
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

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		float wavyAmount = 12;

		float2 resultCoord = texCoord;
		resultCoord.x += sin( texCoord.y * wavyAmount + g_flTime * wavyAmount ) * wavyAmount * 0.005;

		float4 resultColor = g_tBaseTexture.Sample( g_sSparkWrap, resultCoord ) * color;
		float4 redValue = float4( 1, 0, 0, 2 * resultColor.a );

		resultColor = resultColor * 1;
	}

	#include "ns2/gui_output.hlsl"
}
