HEADER
{
	Description = "NS2 flash";
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
	Texture2D g_tBaseTexture < Attribute( "Texture" ); Channel( RGBA, Box( BaseTexture ), Linear ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	float g_flMultAmount < Default1( 1.0 ); >;
	float g_flScreenAmount < Default1( 0.0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		float4 readIn = g_tBaseTexture.Sample( g_sSparkWrap, texCoord );
		readIn.rgb = 1.0f - ( ( 1.0f - readIn.rgb ) * ( 1.0f - g_flScreenAmount ) );
		readIn.rgb *= g_flMultAmount;
		resultColor = readIn * color;
	}

	#include "ns2/gui_output.hlsl"
}
