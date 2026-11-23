HEADER
{
	Description = "NS2 missioncompletionobj";
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
		float glowSizeY = 0.02;
		float currentY = texCoord.y;
		float4 backgroundColor = float4( 0.729, 0.333, 0.827, 0.04 );
		float4 borderGlowColor = float4( 0.121, 0.129, 0.137, 1 );
		float4 resultColor = backgroundColor;

		float glowDistance = min( abs( currentY - glowSizeY ), 1 - currentY );

		float isTopBorder = step( currentY, glowSizeY );
		float isBottomBorder = step( 1 - glowSizeY, currentY );

		if ( isTopBorder || isBottomBorder )
		{
			resultColor = borderGlowColor;

			if ( isTopBorder )
			{
				float lerpAmount = currentY / glowSizeY;
				resultColor.a = lerp( 1, 0, lerpAmount );
			}
			else
			{
				float bottomGlowStart = 1 - glowSizeY;
				float normY = currentY - bottomGlowStart;

				float lerpAmount = normY / glowSizeY;
				resultColor.a = lerp( 0, 1, lerpAmount );
			}
		}

		resultColor = resultColor;
	}

	#include "ns2/gui_output.hlsl"
}
