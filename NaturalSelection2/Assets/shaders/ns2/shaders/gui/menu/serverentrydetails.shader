HEADER
{
	Description = "NS2 serverentrydetails";
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
	float g_flOpacity < Default1( 1 ); >;
	float2 g_vBoxSize < Default2( 300, 200 ); >;
	float g_flTabHeight < Default1( 30 ); >;
	float g_flHighWidth < Default1( 100 ); >;
	float g_flStrokeWidth < Default1( 2 ); >;
	float4 g_vStrokeColor < Default4( 0.5529411765, 0.5882352941, 0.6274509804, 0.5 ); >;
	float4 g_vGradColor1 < Default4( 0, 0, 0, 0 ); >;
	float4 g_vGradColor2 < Default4( 0.1490196078, 0.168627451, 0.2078431373, 1.0 ); >;

	float4 Unpremult( float4 x )
	{
		return float4( x.a == 0.0f ? float3( 0, 0, 0 ) : x.rgb / x.a, x.a );
	}

	float2 SnapPointToShape( float2 pxPos )
	{

		float2 pxULCorner = ( g_vSurfaceSize - g_vBoxSize ) * 0.5f;

		float2 pxLRCorner = pxULCorner + g_vBoxSize;

		float2 snapPos;
		if ( pxPos.x < pxULCorner.x )
		{
			if ( pxPos.y < pxULCorner.y )
				snapPos = pxULCorner;
			else if ( pxPos.y >= pxLRCorner.y )
				snapPos = float2( pxULCorner.x, pxLRCorner.y );
			else
				snapPos = float2( pxULCorner.x, pxPos.y );
		}
		else if ( pxPos.x >= pxLRCorner.x )
		{
			if ( pxPos.y < pxULCorner.y + g_flTabHeight )
				snapPos = float2( pxLRCorner.x, pxULCorner.y + g_flTabHeight );
			else if ( pxPos.y > pxLRCorner.y )
				snapPos = pxLRCorner;
			else
				snapPos = float2( pxLRCorner.x, pxPos.y );
		}
		else
		{
			float midY = min( max( ( pxPos.x - pxULCorner.x ) - g_flHighWidth, 0.0 ), g_flTabHeight ) + pxULCorner.y;
			if ( pxPos.y < midY )
			{
				if ( pxPos.x < pxULCorner.x + g_flHighWidth )
					snapPos = float2( pxPos.x, pxULCorner.y );
				else
				{

					const float2 diagVec = float2( -0.7071067812f, -0.7071067812f );
					float2 topDiag = float2( pxULCorner.x + g_flHighWidth, pxULCorner.y );
					if ( dot( diagVec, pxPos ) >= dot( diagVec, topDiag ) )
						snapPos = topDiag;
					else
					{

						const float2 bisectVec = float2( 0.3826834324f, -0.9238795325f );
						const float2 perpVec = float2( bisectVec.y, -bisectVec.x );
						float2 bottomDiag = topDiag + float2( g_flTabHeight, g_flTabHeight );
						if ( dot( perpVec, pxPos ) > dot( perpVec, bottomDiag ) )
						{

							float d0 = dot( diagVec, bottomDiag );
							float d1 = dot( diagVec, topDiag );
							float d = dot( diagVec, pxPos );
							float t = saturate( ( d - d0 ) / ( d1 - d0 ) );
							snapPos = bottomDiag * ( 1.0f - t ) + topDiag * t;
						}
						else
							snapPos = float2( pxPos.x, pxULCorner.y + g_flTabHeight );
					}
				}
			}
			else if ( pxPos.y >= pxLRCorner.y )
				snapPos = float2( pxPos.x, pxLRCorner.y );
			else
				snapPos = pxPos;
		}

		return snapPos;
	}

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		float2 pxPos = texCoord * g_vSurfaceSize;

		float dist = distance( pxPos, SnapPointToShape( pxPos ) );

		float gradVal = saturate( ( texCoord.x * 0.25f ) + 0.375f );
		float4 gradColor = g_vGradColor1 * ( 1.0f - gradVal ) + g_vGradColor2 * gradVal;
		gradColor *= saturate( 1.0f - dist );

		float strokeValue = saturate( -abs( dist - g_flStrokeWidth * 0.5f - 0.5f ) + g_flStrokeWidth * 0.5f + 0.5f );
		strokeValue = 2.0f * strokeValue - strokeValue * strokeValue;
		float4 strokeColorOut = g_vStrokeColor * strokeValue;

		resultColor = Unpremult( ( gradColor + strokeColorOut ) * g_flOpacity );
	}

	#include "ns2/gui_output.hlsl"
}
