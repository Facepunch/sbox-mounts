HEADER
{
	Description = "NS2 textgradient";
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
	float g_flOpacity < Default1( 1.0 ); >;
	float g_flGradScale < Default1( 1.0 ); >;
	float4 g_vTextColor < Default4( 1, 1, 1, 1 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		float textRead = g_tBaseTexture.Sample( g_sSparkWrap, texCoord ).r;
		float4 text = float4( 1, 1, 1, textRead ) * g_vTextColor;
		float grad = saturate( ( ( texCoord.y - 0.5f ) / g_flGradScale ) + 0.5f );
		text.rgb = text.rgb * ( 1.0f - g_flOpacity ) + grad * g_flOpacity;
		resultColor = text * color;
	}

	#include "ns2/gui_output.hlsl"
}
