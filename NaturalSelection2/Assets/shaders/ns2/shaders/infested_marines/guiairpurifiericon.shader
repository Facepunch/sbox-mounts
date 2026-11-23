HEADER
{
	Description = "NS2 guiairpurifiericon";
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
	float g_flPulseInfluence < Default1( 1 ); >;
	float g_flPulseFreq < Default1( 1 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		const float pi = 3.14159265359f;

		float intensity = ( sin( g_flTime * g_flPulseFreq * pi * 2 ) * 0.5f + 0.5f ) * g_flPulseInfluence;

		float4 icon = g_tBaseTexture.Sample( g_sSparkWrap, texCoord );
		float4 result = icon * ( 1.0f - intensity ) + color * intensity;
		result.a = icon.a;

		resultColor = result;
	}

	#include "ns2/gui_output.hlsl"
}
