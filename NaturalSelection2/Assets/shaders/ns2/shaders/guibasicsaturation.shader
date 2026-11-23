HEADER
{
	Description = "NS2 guibasicsaturation";
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
	float g_flSaturation < Default1( 1.0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		float4 result;
		if ( textured )
		{
			result = g_tBaseTexture.Sample( g_sSparkWrap, texCoord ) * color;
		}
		else
		{
			result = color;
		}

		float lum = result.r * 0.21f + result.g * 0.72f + result.b * 0.07f;
		result.rgb = ( result.rgb * g_flSaturation ) + ( lum * ( 1.0f - g_flSaturation ) );

		resultColor = result;
	}

	#include "ns2/gui_output.hlsl"
}
