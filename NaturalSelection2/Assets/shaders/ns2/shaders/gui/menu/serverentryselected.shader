HEADER
{
	Description = "NS2 serverentryselected";
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
	float g_flOpacity < Default1( 0.43 ); >;
	float2 g_vBoxSize < Default2( 300, 300 ); >;
	float g_flStrokeWidth < Default1( 2 ); >;
	float3 g_vStrokeColor < Default3( 0, 0.921569, 1 ); >;
	float g_flFillOpacity < Default1( 0.51 ); >;
	float3 g_vGradColor1 < Default3( 0.0549, 0.2863, 0.3725 ); >;
	float3 g_vGradColor2 < Default3( 0.1294, 0.3569, 0.4471 ); >;

	float4 Unpremult( float4 input )
	{
		input.rgb = input.a > 0.0f ? input.rgb / input.a : 0.0f;
		return input;
	}
	float4 Premult( float4 input )
	{
		return float4( input.rgb * input.a, input.a );
	}
	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		float2 pxPos = texCoord * g_vSurfaceSize;

		float2 pxULCorner = ( g_vSurfaceSize - g_vBoxSize ) * 0.5f;
		float2 pxLRCorner = pxULCorner + g_vBoxSize;

		float2 pxSnapPos =
			float2( max( min( pxPos.x, pxLRCorner.x ), pxULCorner.x ), max( min( pxPos.y, pxLRCorner.y ), pxULCorner.y ) );

		float dist = distance( pxPos, pxSnapPos );
		float strokeIntensity = saturate( -abs( dist - g_flStrokeWidth * 0.5f - 0.5f ) + g_flStrokeWidth * 0.5f + 0.5f );

		float4 stroke = Premult( float4( g_vStrokeColor, strokeIntensity ) );

		float fillIntensity = saturate( 1.0f - dist ) * g_flFillOpacity;

		float3 gradColor = g_vGradColor1 * ( 1.0f - texCoord.y ) + g_vGradColor2 * texCoord.y;
		float4 gradient = Premult( float4( gradColor, fillIntensity ) );

		float4 composite = ( ( stroke + gradient ) * g_flOpacity ) * color;

		resultColor = composite;
	}

	#include "ns2/gui_output.hlsl"
}
