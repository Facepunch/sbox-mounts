HEADER
{
	Description = "NS2 coolbox";
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

		const float strokeWidth = 2.0f;
		const float4 strokeColor = float4( 141.0f / 255.0f, 150.0f / 255.0f, 160.0f / 255.0f, 0.5f );

		const float shadowRadius = 20.0;
		const float shadowGaussCSqr = 32.08001667f;
		const float4 shadowColor = float4( 7.0f / 255.0f, 10.0f / 255.0f, 15.0f / 255.0f, 0.5f );
		const float2 shadowOffset = float2( 0.0f, 2.5f );

		const float glowRadius = 40.0;
		const float glowGaussCSqr = 128.320066674f;
		const float4 glowColor = float4( 171.0f / 255.0f, 189.0f / 255.0f, 204.0f / 255.0f, 0.1f );

		const float4 gradMin = float4( 5.0f / 255.0f, 7.0f / 255.0f, 12.0f / 255.0f, 0.0f );
		const float4 gradMax = float4( 38.0f / 255.0f, 43.0f / 255.0f, 53.0f / 255.0f, 0.85f );

		float2 pxPos = texCoord * g_vSurfaceSize;

		float2 strokePxBoxMin = ( g_vSurfaceSize - ( g_vBoxSize + float2( 2, 2 ) ) ) * 0.5f;
		float2 strokePxBoxMax = g_vBoxSize + strokePxBoxMin + float2( 1, 1 );

		float2 strokePxBoxPos = max( min( pxPos, strokePxBoxMax ), strokePxBoxMin );

		float strokeWidthSq = strokeWidth * strokeWidth;
		float strokeOpacity = 0.0f;
		float2 strokeDiff = strokePxBoxPos - pxPos;

		float strokeDistSq = dot( strokeDiff, strokeDiff );
		if ( strokeDistSq >= 0.000001f && strokeDistSq <= strokeWidthSq )
		{
			strokeOpacity = saturate( ( 0.75f * strokeWidth ) - abs( sqrt( strokeDistSq ) - 0.75f * strokeWidth ) );
		}
		strokeOpacity = strokeOpacity * strokeColor.a;

		float boxOpacity = color.a * saturate( 1.0f - max( abs( strokeDiff.x ) - 0.5f, abs( strokeDiff.y ) - 0.5f ) );

		float shadowOpacity = 0.0f;
		if ( strokeDistSq >= strokeWidthSq )
		{
			float2 shadowPxBoxSize = g_vBoxSize - ( float2( shadowRadius, shadowRadius ) * 0.5 );
			float2 shadowPxBoxMin = ( g_vSurfaceSize - shadowPxBoxSize ) * 0.5f + shadowOffset;
			float2 shadowPxBoxMax = shadowPxBoxMin + shadowPxBoxSize;

			float2 shadowPxBoxPos = max( min( pxPos, shadowPxBoxMax ), shadowPxBoxMin );
			float2 shadowDiff = pxPos - shadowPxBoxPos;

			if ( abs( shadowDiff.x ) <= shadowRadius && abs( shadowDiff.y ) <= shadowRadius )
			{
				shadowOpacity = Gauss( shadowGaussCSqr, abs( shadowDiff.x ) ) * Gauss( shadowGaussCSqr, abs( shadowDiff.y ) );
			}
		}
		float4 shadow = saturate( float4( shadowColor.rgb * shadowColor.a, shadowColor.a ) * shadowOpacity );

		float glowOpacity = 0.0f;
		float2 diff = float2( 0.0f, 0.0f );
		if ( strokeDistSq < 0.000001f )
		{
			diff = min( pxPos - strokePxBoxMin, strokePxBoxMax - pxPos );
			if ( diff.x <= glowRadius || diff.y <= glowRadius )
			{

				glowOpacity = 1.0f - ( ( 1.0f - saturate( Gauss( glowGaussCSqr, max( diff.x, 0.0f ) ) ) ) *
									   ( 1.0f - saturate( Gauss( glowGaussCSqr, max( diff.y, 0.0f ) ) ) ) );
				glowOpacity = glowOpacity * glowOpacity;
			}
		}
		float4 glow = float4( glowColor.rgb * glowColor.a, glowColor.a ) * glowOpacity;

		float4 gradient = gradMin + ( ( gradMax - gradMin ) * texCoord.y ) * boxOpacity;

		float4 result = float4( color.rgb * ( 1.0f - strokeOpacity ) + strokeColor.rgb * strokeOpacity,
								strokeOpacity + ( ( 1.0 - strokeOpacity ) * boxOpacity ) );

		result = result * ( 1.0f - shadow.a ) + shadow;

		result = result * ( 1.0f - gradient.a ) + gradient;

		result = result * ( 1.0f - glow.a ) + glow;

		result.a *= g_flOpacity;

		resultColor = result;
	}

	#include "ns2/gui_output.hlsl"
}
