HEADER
{
	Description = "NS2 distancefieldfont";
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
	float g_flSmoothing < Default1( 1.0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		if ( textured )
		{

			float4 field = g_tBaseTexture.Sample( g_sSparkWrap, texCoord );
			float distance = field.a < 1.0f ? field.a : field.r;
			float alpha = smoothstep( 0.5f - g_flSmoothing, 0.5f + g_flSmoothing, distance );
			resultColor = float4( color.rgb, alpha * color.a );
		}
		else
		{
			resultColor = color;
		}
	}

	#include "ns2/gui_output.hlsl"
}
