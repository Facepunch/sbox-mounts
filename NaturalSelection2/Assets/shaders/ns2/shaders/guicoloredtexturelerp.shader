HEADER
{
	Description = "NS2 guicoloredtexturelerp";
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
	float g_flLerpFactor < Default1( 0 ); >;
	float g_flFading < Default1( 0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		float4 textureColor = g_tBaseTexture.Sample( g_sSparkWrap, texCoord );
		float textureAlpha = textureColor.a;

		float newR = lerp( textureColor.r, color.r, g_flLerpFactor );
		float newG = lerp( textureColor.g, color.g, g_flLerpFactor );
		float newB = lerp( textureColor.b, color.b, g_flLerpFactor );

		if ( g_flFading > 0 )
		{
			textureAlpha = textureColor.a * color.a;
		}

		resultColor = float4( newR, newG, newB, textureAlpha );
	}

	#include "ns2/gui_output.hlsl"
}
