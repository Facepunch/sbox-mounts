HEADER
{
	Description = "NS2 outlinedbox";
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
	float2 g_vCornerOffset < Default2( 0, 0 ); >;
	float2 g_vBoxSize < Default2( 128, 128 ); >;
	float g_flOpacity < Default1( 1.0 ); >;
	float4 g_vStrokeColor < Default4( 1, 1, 1, 1 ); >;
	float g_flStrokeWidth < Default1( 2.0 ); >;

	float4 Unpremult( float4 input )
	{
		input.rgb = input.a > 0.0f ? input.rgb / input.a : 0.0f;
		return input;
	}
	float4 Premult( float4 x )
	{
		return float4( x.r * x.a, x.g * x.a, x.b * x.a, x.a );
	}

	float CalculatePixelOverlapArea( float2 pxPos, float2 c0, float2 c1 )
	{
		float2 minCorner = float2( max( pxPos.x, c0.x ), max( pxPos.y, c0.y ) );
		float2 maxCorner = float2( min( pxPos.x + 1.0f, c1.x ), min( pxPos.y + 1.0f, c1.y ) );
		return max( maxCorner.x - minCorner.x, 0.0f ) * max( maxCorner.y - minCorner.y, 0.0f );
	}

	float CalculatePixelOverlapCorner( float2 pxPos, float2 c, float r )
	{

		float2 c0 = ( pxPos - c ) / r;
		float2 c1 = c0 + ( float2( 1.0f, 1.0f ) / r );

		c0.x = max( c0.x, 0.0f );
		c0.y = max( c0.y, 0.0f );
		c1.x = max( c1.x, 0.0f );
		c1.y = max( c1.y, 0.0f );

		if ( c0.x * c0.x + c0.y * c0.y >= 1.0 )
			return 0.0;
		else if ( c1.x * c1.x + c1.y * c1.y <= 1.0 )
			return ( ( c1.x - c0.x ) * ( c1.y - c0.y ) ) * r * r;
		else
		{

			float2 clippedC0 = c0;
			float2 clippedC1 = c1;

			float topEdgeRootPart = 1.0f - ( c0.y * c0.y );
			if ( topEdgeRootPart >= 0.0f )
				clippedC1.x = min( clippedC1.x, sqrt( topEdgeRootPart ) );

			float leftEdgeRootPart = 1.0f - ( c0.x * c0.x );
			if ( leftEdgeRootPart >= 0.0f )
				clippedC1.y = min( clippedC1.y, sqrt( leftEdgeRootPart ) );

			float bottomEdgeRootPart = 1.0f - ( c1.y * c1.y );
			if ( bottomEdgeRootPart >= 0.0f )
				clippedC0.x = max( clippedC0.x, sqrt( bottomEdgeRootPart ) );

			float rightEdgeRootPart = 1.0f - ( c1.x * c1.x );
			if ( rightEdgeRootPart >= 0.0 )
				clippedC0.y = max( clippedC0.y, sqrt( rightEdgeRootPart ) );

			float area1 = ( asin( clippedC1.x ) + clippedC1.x * sqrt( 1.0f - ( clippedC1.x * clippedC1.x ) ) ) * 0.5f;
			float area0 = ( asin( clippedC0.x ) + clippedC0.x * sqrt( 1.0f - ( clippedC0.x * clippedC0.x ) ) ) * 0.5f;
			float areaUnder = ( clippedC1.x - clippedC0.x ) * clippedC0.y;
			float area = saturate( ( area1 - area0 ) - areaUnder );

			area += saturate( ( clippedC0.x - c0.x ) * ( clippedC0.y - c0.y ) );
			area += saturate( ( clippedC0.x - c0.x ) * ( clippedC1.y - clippedC0.y ) );
			area += saturate( ( clippedC1.x - clippedC0.x ) * ( clippedC0.y - c0.y ) );

			return area * r * r;
		}
	}

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		const float filterFactor = 1.5f;

		float2 fSurfaceSize = g_vSurfaceSize / filterFactor;
		float2 fBoxSize = g_vBoxSize / filterFactor;
		float fStrokeWidth = g_flStrokeWidth / filterFactor;
		float2 fCornerOffset = g_vCornerOffset / filterFactor;

		float2 pxPos = texCoord * fSurfaceSize - float2( 0.5f, 0.5f );
		float2 pxULCorner = fCornerOffset;
		float2 pxLRCorner = pxULCorner + fBoxSize;
		float2 pxURCorner = float2( pxLRCorner.x, pxULCorner.y );
		float2 pxLLCorner = float2( pxULCorner.x, pxLRCorner.y );

		float fillFactor = saturate( CalculatePixelOverlapArea( pxPos, pxULCorner, pxLRCorner ) );

		float sArea = 0.0f;

		sArea += saturate(
			CalculatePixelOverlapArea( pxPos, pxULCorner - float2( fStrokeWidth, 0 ), float2( pxULCorner.x, pxLRCorner.y ) ) );

		sArea += saturate(
			CalculatePixelOverlapArea( pxPos, pxULCorner - float2( 0, fStrokeWidth ), float2( pxLRCorner.x, pxULCorner.y ) ) );

		sArea += saturate( CalculatePixelOverlapArea( pxPos, pxURCorner, pxLRCorner + float2( fStrokeWidth, 0 ) ) );

		sArea += saturate( CalculatePixelOverlapArea( pxPos, pxLLCorner, pxLRCorner + float2( 0, fStrokeWidth ) ) );

		sArea += saturate( CalculatePixelOverlapCorner( pxPos, pxLRCorner, fStrokeWidth ) );
		sArea += saturate( CalculatePixelOverlapCorner( float2( -pxPos.x, pxPos.y ),
														float2( 1.0f - pxLLCorner.x, pxLLCorner.y ), fStrokeWidth ) );
		sArea += saturate( CalculatePixelOverlapCorner( float2( pxPos.x, -pxPos.y ),
														float2( pxURCorner.x, 1.0f - pxURCorner.y ), fStrokeWidth ) );
		sArea += saturate( CalculatePixelOverlapCorner( float2( -pxPos.x, -pxPos.y ),
														float2( 1.0f - pxULCorner.x, 1.0f - pxULCorner.y ), fStrokeWidth ) );

		float4 sColor = Premult( g_vStrokeColor ) * sArea;

		float4 background = Premult( color ) * fillFactor;
		background = background * ( 1.0f - sColor.a ) + sColor;

		resultColor = Unpremult( background );
		resultColor.a *= g_flOpacity;
	}

	#include "ns2/gui_output.hlsl"
}
