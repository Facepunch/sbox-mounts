HEADER
{
	Description = "NS2 serverbrowserbox";
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
	float2 g_vBoxSize < Default2( 128, 128 ); >;
	float2 g_vTabSize < Default2( 64, 32 ); >;
	float g_flStrokeWidth < Default1( 2 ); >;
	float4 g_vStrokeColor < Default4( 0.5529411765, 0.5882352941, 0.6274509804, 0.5 ); >;
	float4 g_vGradColor1 < Default4( 0, 0, 0, 0 ); >;
	float4 g_vGradColor2 < Default4( 0.1490196078, 0.168627451, 0.2078431373, 1.0 ); >;
	float g_flShadowRadius < Default1( 15 ); >;
	float4 g_vShadowColor < Default4( 0.0274509804, 0.0392156863, 0.0588235294, 0.5 ); >;
	float2 g_vShadowOffset < Default2( 0, 7.5 ); >;
	float g_flGlowRadius < Default1( 10 ); >;
	float4 g_vGlowColor < Default4( 0.6705882353, 0.7411764706, 0.8, 0.1 ); >;

	float2 SnapPointToLineSegment( float2 pxPos, float2 p1, float2 p2, float2 lineVec )
	{
		float d1 = dot( p1, lineVec );
		float d2 = dot( p2, lineVec );
		float dp = dot( pxPos, lineVec );
		float t = saturate( ( dp - d2 ) / ( d1 - d2 ) );
		return p2 * ( 1.0f - t ) + p1 * t;
	}

	float Screen( float a, float b )
	{
		return 1.0 - ( ( 1.0 - a ) * ( 1.0 - b ) );
	}
	float4 Unpremult( float4 x )
	{
		return float4( x.a == 0.0f ? float3( 0, 0, 0 ) : x.rgb / x.a, x.a );
	}
	float4 Premult( float4 x )
	{
		return float4( x.r * x.a, x.g * x.a, x.b * x.a, x.a );
	}

	float Gauss( float x, float r )
	{
		float n = -x * x;

		float c = 0.2831961187 * r;
		float d = c * c * 2.0f;
		return exp( n / d );
	}

	float2 SnapPointToShape( float2 pxPos, float2 pxULCorner, float2 pxLRCorner )
	{
		float tabX = ( g_vSurfaceSize.x - g_vTabSize.x ) * 0.5f;
		float2 snapPos;
		float2 pxPosM = pxPos;

		if ( pxPosM.x > g_vSurfaceSize.x * 0.5f )
			pxPosM.x = 2.0f * ( g_vSurfaceSize.x * 0.5f ) - pxPosM.x;

		if ( pxPosM.x < pxULCorner.x )
		{
			if ( pxPosM.y < pxULCorner.y )
				snapPos = pxULCorner;
			else if ( pxPosM.y > pxLRCorner.y )
				snapPos = float2( pxULCorner.x, pxLRCorner.y );
			else
				snapPos = float2( pxULCorner.x, pxPosM.y );
		}
		else
		{
			if ( pxPosM.y < pxULCorner.y )
				snapPos = float2( pxPosM.x, pxULCorner.y );
			else if ( pxPosM.y < pxLRCorner.y )
				snapPos = pxPosM;
			else if ( pxPosM.x < tabX )
			{

				const float2 perpVec = float2( -0.9238795325, -0.3826834324 );

				if ( dot( perpVec, pxPosM ) > dot( perpVec, float2( tabX - g_vTabSize.y, pxLRCorner.y ) ) )
					snapPos = float2( pxPosM.x, pxLRCorner.y );
				else
				{
					const float2 perpVec2 = float2( 0.7071067812f, -0.7071067812f );
					const float2 diagVec = float2( -0.7071067812f, -0.7071067812f );
					if ( dot( perpVec2, pxPosM ) > dot( perpVec2, float2( tabX, pxLRCorner.y + g_vTabSize.y ) ) )
						snapPos = pxPosM;
					else if ( dot( diagVec, pxPosM ) > dot( diagVec, float2( tabX, pxLRCorner.y + g_vTabSize.y ) ) )
					{
						float diagUL = dot( diagVec, float2( tabX - g_vTabSize.y, pxLRCorner.y ) );
						float diagLR = dot( diagVec, float2( tabX, pxLRCorner.y + g_vTabSize.y ) );
						float diagP = dot( diagVec, pxPosM );
						float t = saturate( ( diagP - diagLR ) / ( diagUL - diagLR ) );
						snapPos = float2( tabX, pxLRCorner.y + g_vTabSize.y ) * ( 1.0f - t ) +
								  float2( tabX - g_vTabSize.y, pxLRCorner.y ) * t;
					}
					else
						snapPos = float2( tabX, pxLRCorner.y + g_vTabSize.y );
				}
			}
			else
				snapPos = float2( pxPosM.x, min( pxPosM.y, pxLRCorner.y + g_vTabSize.y ) );
		}

		if ( pxPos.x > g_vSurfaceSize.x * 0.5f )
			snapPos.x = 2.0f * ( g_vSurfaceSize.x * 0.5f ) - snapPos.x;

		return snapPos;
	}

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		float2 pxPos = texCoord * g_vSurfaceSize;

		float2 pxULCorner = ( g_vSurfaceSize - g_vBoxSize ) * 0.5f;

		float2 pxLRCorner = pxULCorner + ( g_vBoxSize - 1.0f ) - float2( 0, g_vTabSize.y );

		float dist = distance( pxPos, SnapPointToShape( pxPos, pxULCorner, pxLRCorner ) );
		float fillFactor = saturate( 1.0f - dist );

		float4 gradColor = Premult( g_vGradColor1 ) * ( 1.0f - texCoord.y ) + Premult( g_vGradColor2 ) * texCoord.y;
		gradColor *= fillFactor;

		float strokeValue = saturate( -abs( dist - g_flStrokeWidth * 0.5f - 0.5f ) + g_flStrokeWidth * 0.5f + 0.5f );
		float4 sColor = Premult( g_vStrokeColor ) * strokeValue;

		float2 pxShadowPos = pxPos - g_vShadowOffset;
		float2 shadowSnapPos = SnapPointToShape( pxShadowPos, pxULCorner + float2( g_flShadowRadius * 0.75f, g_flShadowRadius ),
												 pxLRCorner - float2( g_flShadowRadius * 0.75f, g_flShadowRadius ) );
		float2 shadowDiff = abs( shadowSnapPos - pxShadowPos );
		shadowDiff.x = Gauss( shadowDiff.x, g_flShadowRadius * 2.0 );
		shadowDiff.y = Gauss( shadowDiff.y, g_flShadowRadius * 2.0 );
		float shadowCoverage = shadowDiff.x * shadowDiff.y * ( 1.0f - fillFactor );
		float4 shadColor = Premult( g_vShadowColor ) * shadowCoverage;

		float glowFactor = 0.0f;
		float2 gDist;

		gDist = abs( pxPos -
					 SnapPointToLineSegment( pxPos, pxULCorner, float2( pxLRCorner.x, pxULCorner.y ), float2( 1.0f, 0.0f ) ) );
		glowFactor = Screen( glowFactor, Gauss( gDist.y, g_flGlowRadius ) );

		gDist = abs( pxPos -
					 SnapPointToLineSegment( pxPos, float2( pxLRCorner.x, pxULCorner.y ), pxLRCorner, float2( 0.0f, 1.0f ) ) );
		glowFactor = Screen( glowFactor, Gauss( gDist.x, g_flGlowRadius ) );

		gDist = abs( pxPos - SnapPointToLineSegment(
								 pxPos, pxLRCorner,
								 float2( g_vSurfaceSize.x * 0.5f + g_vTabSize.x * 0.5f + g_vTabSize.y, pxLRCorner.y ),
								 float2( -1.0f, 0.0f ) ) );
		glowFactor = Screen( glowFactor, Gauss( gDist.x, g_flGlowRadius ) * Gauss( gDist.y, g_flGlowRadius ) );

		gDist = abs( pxPos - SnapPointToLineSegment(
								 pxPos, float2( g_vSurfaceSize.x * 0.5f + g_vTabSize.x * 0.5f + g_vTabSize.y, pxLRCorner.y ),
								 float2( g_vSurfaceSize.x * 0.5f + g_vTabSize.x * 0.5f, pxLRCorner.y + g_vTabSize.y ),
								 float2( -0.7071067812f, 0.7071067812f ) ) );
		glowFactor = max( glowFactor, Gauss( gDist.x * 0.7071067812f + gDist.y * 0.7071067812f, g_flGlowRadius ) *
										  Gauss( gDist.x * 0.7071067812f - gDist.y * 0.7071067812f, g_flGlowRadius ) );

		gDist = abs( pxPos - SnapPointToLineSegment(
								 pxPos, float2( g_vSurfaceSize.x * 0.5f + g_vTabSize.x * 0.5f, pxLRCorner.y + g_vTabSize.y ),
								 float2( g_vSurfaceSize.x * 0.5f - g_vTabSize.x * 0.5f, pxLRCorner.y + g_vTabSize.y ),
								 float2( -1.0f, 0.0f ) ) );
		glowFactor = Screen( glowFactor, Gauss( gDist.x, g_flGlowRadius ) * Gauss( gDist.y, g_flGlowRadius ) );

		gDist = abs( pxPos - SnapPointToLineSegment(
								 pxPos, float2( g_vSurfaceSize.x * 0.5f - g_vTabSize.x * 0.5f, pxLRCorner.y + g_vTabSize.y ),
								 float2( g_vSurfaceSize.x * 0.5f - g_vTabSize.x * 0.5f - g_vTabSize.y, pxLRCorner.y ),
								 float2( -0.7071067812f, -0.7071067812f ) ) );
		glowFactor = Screen( glowFactor, Gauss( gDist.x * 0.7071067812f + gDist.y * 0.7071067812f, g_flGlowRadius ) *
											 Gauss( gDist.x * 0.7071067812f - gDist.y * 0.7071067812f, g_flGlowRadius ) );

		gDist = abs( pxPos - SnapPointToLineSegment(
								 pxPos, float2( g_vSurfaceSize.x * 0.5f - g_vTabSize.x * 0.5f - g_vTabSize.y, pxLRCorner.y ),
								 float2( pxULCorner.x, pxLRCorner.y ), float2( -1.0f, 0.0f ) ) );
		glowFactor = max( glowFactor, Gauss( gDist.x, g_flGlowRadius ) * Gauss( gDist.y, g_flGlowRadius ) );

		gDist = abs( pxPos -
					 SnapPointToLineSegment( pxPos, float2( pxULCorner.x, pxLRCorner.y ), pxULCorner, float2( 0.0f, -1.0f ) ) );
		glowFactor = Screen( glowFactor, Gauss( gDist.x, g_flGlowRadius ) );

		glowFactor = glowFactor * fillFactor;
		float4 glowCol = Premult( g_vGlowColor ) * glowFactor;

		float4 outputColor;
		outputColor = gradColor + sColor;
		outputColor = shadColor * ( 1.0f - outputColor.a ) + outputColor;
		outputColor = glowCol + outputColor * ( 1.0f - glowCol.a );

		resultColor = Unpremult( outputColor * g_flOpacity );
	}

	#include "ns2/gui_output.hlsl"
}
