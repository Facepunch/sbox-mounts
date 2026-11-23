HEADER
{
	Description = "NS2 simplebox";
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
	float g_flOpacity < Default1( 1.0 ); >;
	float g_flStrokeWidth < Default1( 2.0 ); >;

	float4 Unpremult( float4 input )
	{
		input.rgb = input.a > 0.0f ? input.rgb / input.a : 0.0f;
		return input;
	}
	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		float2 pxPos = ( texCoord * g_vSurfaceSize );

		float strokeOpacity = 0.0f;
		strokeOpacity = saturate( min( -pxPos.x + 0.5f + g_flStrokeWidth, pxPos.x + 0.5f ) );
		strokeOpacity = max( strokeOpacity, saturate( min( -pxPos.y + 0.5f + g_flStrokeWidth, pxPos.y + 0.5f ) ) );
		strokeOpacity =
			max( strokeOpacity,
				 saturate( min( -pxPos.x + 0.5f + g_vSurfaceSize.x, pxPos.x + 0.5f - g_vSurfaceSize.x + g_flStrokeWidth ) ) );
		strokeOpacity =
			max( strokeOpacity,
				 saturate( min( -pxPos.y + 0.5f + g_vSurfaceSize.y, pxPos.y + 0.5f - g_vSurfaceSize.y + g_flStrokeWidth ) ) );
		strokeOpacity *= g_flOpacity;

		resultColor = color;
		resultColor.a = strokeOpacity * color.a;
	}

	#include "ns2/gui_output.hlsl"
}
