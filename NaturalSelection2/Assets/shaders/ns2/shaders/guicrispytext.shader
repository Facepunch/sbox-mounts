HEADER
{
	Description = "NS2 guicrispytext";
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
	float g_flCrispy < Default1( 1.0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		float readIn = g_tBaseTexture.Sample( g_sSparkWrap, texCoord ).a;

		resultColor = color * float4( 1.0f, 1.0f, 1.0f, saturate( g_flCrispy * ( readIn - 0.5 ) + 0.5 ) );
	}

	#include "ns2/gui_output.hlsl"
}
