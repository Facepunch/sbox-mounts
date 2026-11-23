HEADER
{
	Description = "NS2 guisidebysidergbavideo";
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
		float3 rgbRead = g_tBaseTexture.Sample( g_sSparkWrap, float2( texCoord.x * 0.5, texCoord.y ) ).rgb;
		float alphaRead = g_tBaseTexture.Sample( g_sSparkWrap, float2( texCoord.x * 0.5 + 0.5, texCoord.y ) ).r;

		resultColor = float4( rgbRead, alphaRead );
	}

	#include "ns2/gui_output.hlsl"
}
