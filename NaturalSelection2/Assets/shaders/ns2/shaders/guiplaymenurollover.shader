HEADER
{
	Description = "NS2 guiplaymenurollover";
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
	float g_flRcpFrameX < Default1( 0.0 ); >;
	float g_flRcpFrameY < Default1( 0.0 ); >;
	float g_flResScale < Default1( 0.0 ); >;
	float g_flOverTime < Default1( 0.0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		const float flashSpeed = 25.0;
		const float flashStrength = 0.5;

		float alpha = g_tBaseTexture.Sample( g_sSparkWrap, texCoord ).a;
		float flash = alpha / ( ( g_flTime - g_flOverTime ) * flashSpeed + 1.0 );
		float flashColor = flashStrength * flash;

		resultColor = float4( flashColor, flashColor, flashColor, 1.0 );
	}

	#include "ns2/gui_output.hlsl"
}
