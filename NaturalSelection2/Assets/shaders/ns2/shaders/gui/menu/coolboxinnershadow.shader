HEADER
{
	Description = "NS2 coolboxinnershadow";
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
	float2 g_vBoxSize < Default2( 128, 128 ); >;
	float2 g_vSurfaceSize < Default2( 132, 132 ); >;
	float g_flOpacity < Default1( 1.0 ); >;
	float4 g_vStrokeColor < Default4( 0.553, 0.588, 0.627, 0.5 ); >;
	float g_flStrokeWidth < Default1( 2 ); >;
	float4 g_vDropShadowColor < Default4( 0.027, 0.039, 0.059, 0.5 ); >;
	float2 g_vDropShadowRadius < Default2( 20, 20 ); >;
	float2 g_vDropShadowCSqr < Default2( 32.08001667, 32.08001667 ); >;
	float g_flInnerShadowOpacity < Default1( 0.75 ); >;
	float2 g_vInnerShadowRadius < Default2( 20, 20 ); >;
	float2 g_vInnerShadowCSqr < Default2( 32.08001667, 32.08001667 ); >;

	float4 Unpremult( float4 input )
	{
		input.rgb = input.a > 0.0f ? input.rgb / input.a : 0.0f;
		return input;
	}
	float4 Premult( float4 input )
	{
		input.rgb *= input.a;
		return input;
	}
	float Gauss( float cSqr, float x )
	{
		const float exp = 2.71828182846;
		return pow( exp, -( ( x * x ) / ( 2.0f * cSqr ) ) );
	}

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		const float eps = 0.000001f;

		const float2 dropShadowOffset = float2( 0.0f, 2.5f );

		const float4 gradMin = float4( 5.0f / 255.0f, 7.0f / 255.0f, 12.0f / 255.0f, 0.0f );
		const float4 gradMax = float4( 38.0f / 255.0f, 43.0f / 255.0f, 53.0f / 255.0f, 0.85f );

		float2 pxPos = texCoord * g_vSurfaceSize;

		float2 strokePxBoxMin = ( g_vSurfaceSize - ( g_vBoxSize + float2( 2, 2 ) ) ) * 0.5f;
		float2 strokePxBoxMax = g_vBoxSize + strokePxBoxMin + float2( 1, 1 );

		float2 strokePxBoxPos = max( min( pxPos, strokePxBoxMax ), strokePxBoxMin );

		float strokeWidthSq = g_flStrokeWidth * g_flStrokeWidth;
		float strokeOpacity = 0.0f;
		float2 strokeDiff = strokePxBoxPos - pxPos;

		float strokeDistSq = dot( strokeDiff, strokeDiff );
		if ( strokeDistSq >= 0.000001f && strokeDistSq <= strokeWidthSq )
		{
			strokeOpacity = saturate( ( 0.75f * g_flStrokeWidth ) - abs( sqrt( strokeDistSq ) - 0.75f * g_flStrokeWidth ) );
		}
		strokeOpacity = strokeOpacity * g_vStrokeColor.a;

		float boxOpacity = color.a * saturate( 1.0f - max( abs( strokeDiff.x ) - 0.5f, abs( strokeDiff.y ) - 0.5f ) );

		float2 pxBoxMin = ( ( g_vSurfaceSize - g_vBoxSize ) * 0.5f ) + float2( 0.5f, 0.5f );
		float2 pxBoxMax = g_vBoxSize + pxBoxMin;
		float4 pxEdgeDiff = float4( abs( pxPos.x - ( pxBoxMin.x - 1.0f ) ), abs( pxPos.y - ( pxBoxMin.y - 1.0f ) ),
									abs( ( pxBoxMax.x + 1.0f ) - pxPos.x ), abs( ( pxBoxMax.y + 1.0f ) - pxPos.y ) );

		float4 shadowOpacity4D = float4( 0.0f, 0.0f, 0.0f, 0.0f );

		if ( pxEdgeDiff.x >= eps && pxEdgeDiff.x <= g_vInnerShadowRadius.x )
		{
			shadowOpacity4D.x = Gauss( g_vInnerShadowCSqr.x, pxEdgeDiff.x );
		}
		if ( pxEdgeDiff.y >= eps && pxEdgeDiff.y <= g_vInnerShadowRadius.y )
		{
			shadowOpacity4D.y = Gauss( g_vInnerShadowCSqr.y, pxEdgeDiff.y );
		}
		if ( pxEdgeDiff.z >= eps && pxEdgeDiff.z <= g_vInnerShadowRadius.x )
		{
			shadowOpacity4D.z = Gauss( g_vInnerShadowCSqr.x, pxEdgeDiff.z );
		}
		if ( pxEdgeDiff.w >= eps && pxEdgeDiff.w <= g_vInnerShadowRadius.y )
		{
			shadowOpacity4D.w = Gauss( g_vInnerShadowCSqr.y, pxEdgeDiff.w );
		}

		float innerShadowAmount = 1.0f - ( ( 1.0f - shadowOpacity4D.x ) * ( 1.0f - shadowOpacity4D.y ) *
										   ( 1.0f - shadowOpacity4D.z ) * ( 1.0f - shadowOpacity4D.w ) );
		innerShadowAmount = innerShadowAmount * g_flInnerShadowOpacity;
		innerShadowAmount *= 0.5f;

		float shadowOpacity = 0.0f;
		if ( strokeDistSq >= strokeWidthSq )
		{
			float2 shadowPxBoxSize = g_vBoxSize - g_vDropShadowRadius * 0.5;
			float2 shadowPxBoxMin = ( g_vSurfaceSize - shadowPxBoxSize ) * 0.5f + dropShadowOffset;
			float2 shadowPxBoxMax = shadowPxBoxMin + shadowPxBoxSize;

			float2 shadowPxBoxPos = max( min( pxPos, shadowPxBoxMax ), shadowPxBoxMin );
			float2 shadowDiff = pxPos - shadowPxBoxPos;

			if ( abs( shadowDiff.x ) <= g_vDropShadowRadius.x && abs( shadowDiff.y ) <= g_vDropShadowRadius.y )
			{
				shadowOpacity =
					Gauss( g_vDropShadowCSqr.x, abs( shadowDiff.x ) ) * Gauss( g_vDropShadowCSqr.y, abs( shadowDiff.y ) );
			}
		}
		float4 dropShadow =
			saturate( float4( g_vDropShadowColor.rgb * g_vDropShadowColor.a, g_vDropShadowColor.a ) * shadowOpacity );

		float4 gradient = gradMin + ( ( gradMax - gradMin ) * texCoord.y );

		float4 result = Premult( float4( color.rgb, boxOpacity ) );

		result = Unpremult( result );
		result.rgb = result.rgb * ( 1.0f - gradient.a ) + gradient.rgb * gradient.a;
		result = Premult( result );

		result.rgb *= ( 1.0f - innerShadowAmount );

		float4 strokeContribution = Premult( float4( g_vStrokeColor.rgb, strokeOpacity ) );
		result = strokeContribution + result * ( 1.0 - strokeContribution.a );

		result += dropShadow * ( 1.0f - result.a );

		result *= g_flOpacity;

		resultColor = Unpremult( result );
	}

	#include "ns2/gui_output.hlsl"
}
