HEADER
{
	Description = "NS2 diagonalsbox";
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
	float2 g_vSurfaceSize < Default2( 132, 132 ); >;
	float g_flLineWidth < Default1( 10.0 ); >;
	float g_flXFactor < Default1( 1.0 ); >;

	float4 Unpremult( float4 input )
	{
		input.rgb = input.a > 0.0f ? input.rgb / input.a : 0.0f;
		return input;
	}
	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		float2 pxPos = texCoord * g_vSurfaceSize;

		float lineValue = ( pxPos.x * g_flXFactor + pxPos.y + ( g_flTime * 10.0f ) ) / g_flLineWidth;
		float opacity = saturate( ( ( 4.0f * abs( lineValue - floor( lineValue ) - 0.5f ) ) - 1.0f ) * g_flLineWidth + 0.5f );

		resultColor = color;
		resultColor.a *= opacity;
	}

	#include "ns2/gui_output.hlsl"
}
