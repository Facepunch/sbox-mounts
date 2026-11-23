HEADER
{
	Description = "NS2 guiwavynomask";
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

		float wavyAmount = 1;
		float2 resultCoord = texCoord;
		resultCoord.x +=
			( ( sin( ( resultCoord.y + cos( g_flTime ) ) * wavyAmount * 10 ) * 0.002 + 0.002 ) / ( 1.0 + 2 * 0.002 ) ) *
			resultCoord.y;
		resultCoord.y +=
			( ( sin( ( resultCoord.x + cos( g_flTime - 0.5 ) ) * wavyAmount * 10 ) * 0.002 + 0.002 ) / ( 1.0 + 2 * 0.002 ) ) *
			resultCoord.x;

		resultColor = g_tBaseTexture.Sample( g_sSparkWrap, resultCoord ) * color;
	}

	#include "ns2/gui_output.hlsl"
}
