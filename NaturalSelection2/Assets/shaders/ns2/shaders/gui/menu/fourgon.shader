HEADER
{
	Description = "NS2 fourgon";
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
	float2 g_vPt0 < Default2( 2, 2 ); >;
	float2 g_vPt1 < Default2( 2, 129 ); >;
	float2 g_vPt2 < Default2( 129, 129 ); >;
	float2 g_vPt3 < Default2( 129, 2 ); >;
	float4 g_vStrokeColor < Default4( 1, 1, 1, 1 ); >;
	float g_flStrokeWidth < Default1( 2.0 ); >;
	float4 g_vGradColor1 < Default4( 1, 1, 1, 1 ); >;
	float4 g_vGradColor2 < Default4( 1, 1, 1, 1 ); >;

	float4 Unpremult( float4 input )
	{
		input.rgb = input.a > 0.0f ? input.rgb / input.a : 0.0f;
		return input;
	}
	float4 Premult( float4 x )
	{
		return float4( x.r * x.a, x.g * x.a, x.b * x.a, x.a );
	}

	float2 Constrain( float2 pxPos, float2 p0, float2 p1, float2 p2, float2 p3 )
	{
		float2 edge0 = p1 - p0;
		float2 edge1 = p2 - p1;
		float2 edge2 = p3 - p2;

		float2 pe0 = float2( edge0.y, -edge0.x );
		float2 pe1 = float2( edge1.y, -edge1.x );
		float2 pe2 = float2( edge2.y, -edge2.x );

		if ( dot( pxPos, pe1 ) < dot( p1, pe1 ) &&
			 ( dot( pxPos, pe2 ) >= dot( p2, pe2 ) || dot( pxPos, edge1 ) < dot( p2, edge1 ) ) )
		{

			if ( dot( pxPos, pe0 ) >= dot( p1, pe0 ) || dot( pxPos, edge1 ) >= dot( p1, edge1 ) )
			{
				float t0 = dot( edge1, p1 );
				float t1 = dot( edge1, p2 );
				float tp = dot( edge1, pxPos );
				float t = saturate( ( tp - t0 ) / ( t1 - t0 ) );
				pxPos = p1 * ( 1.0f - t ) + p2 * t;
			}
			else
				pxPos = p1;
		}

		return pxPos;
	}

	float2 SnapPointToShape( float2 pxPos )
	{
		pxPos = Constrain( pxPos, g_vPt0, g_vPt1, g_vPt2, g_vPt3 );
		pxPos = Constrain( pxPos, g_vPt1, g_vPt2, g_vPt3, g_vPt0 );
		pxPos = Constrain( pxPos, g_vPt2, g_vPt3, g_vPt0, g_vPt1 );
		pxPos = Constrain( pxPos, g_vPt3, g_vPt0, g_vPt1, g_vPt2 );
		return pxPos;
	}

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		float2 pxPos = texCoord * g_vSurfaceSize;

		float2 snapPos = SnapPointToShape( pxPos );
		float dist = distance( snapPos, pxPos );
		float fillFactor = saturate( 1.0f - dist );

		float minY = min( min( g_vPt0.y, g_vPt1.y ), min( g_vPt2.y, g_vPt3.y ) );
		float maxY = max( max( g_vPt0.y, g_vPt1.y ), max( g_vPt2.y, g_vPt3.y ) );
		float gradFrac = ( pxPos.y - minY ) / ( maxY - minY );
		float4 calcGradColor = Premult( g_vGradColor1 * ( 1.0f - gradFrac ) + g_vGradColor2 * gradFrac ) * fillFactor;

		float strokeFactor = saturate( -abs( dist - g_flStrokeWidth * 0.5f - 0.5f ) + g_flStrokeWidth * 0.5f + 0.5f );
		float4 calcStrokeColor = Premult( g_vStrokeColor ) * strokeFactor;

		float4 outputColor;
		outputColor = Premult( color ) * fillFactor;
		outputColor = outputColor * ( 1.0f - calcGradColor.a ) + calcGradColor;
		outputColor = outputColor + calcStrokeColor;

		resultColor = Unpremult( outputColor * g_flOpacity );
	}

	#include "ns2/gui_output.hlsl"
}
