#ifndef NS2_ALPHA_TEST_HLSL
#define NS2_ALPHA_TEST_HLSL

#define DEPTH_STATE_ALREADY_SET
#define BLEND_MODE_ALREADY_SET
#include "common/pixel.hlsl"

RenderState( DepthEnable, true );
RenderState( DepthWriteEnable, S_MODE_DEPTH != 0 );
RenderState( AlphaToCoverageEnable, S_MODE_DEPTH != 0 );
RenderState( BlendEnable, false );
#if S_MODE_DEPTH
	RenderState( DepthFunc, GREATER_EQUAL );
#else
	RenderState( DepthFunc, EQUAL );
#endif

float4 Ns2ShadeAlphaTest( PixelInput i, Material m, float2 textureSize, float reference = 0.5 )
{
	float opacity = saturate( m.Opacity );
	float coverage = opacity;
	if ( g_nMSAASampleCount > 1 )
	{
		float2 texelUv = m.TextureCoords * textureSize;
		float2 dx = ddx( texelUv );
		float2 dy = ddy( texelUv );
		float minification = saturate( 0.5 * log2( max( max( dot( dx, dx ), dot( dy, dy ) ), 1 ) ) );
		float edgeCoverage = saturate( 0.5 + (opacity - reference) / max( fwidth( opacity ), 0.000001 ) );
		coverage = lerp( edgeCoverage, opacity, minification );
		clip( min( opacity, coverage ) - 0.000001 );
	}
	else
	{
		clip( opacity - reference );
		coverage = 1.0;
	}

#if S_MODE_DEPTH
	return float4( 0, 0, 0, coverage );
#else
	m.Opacity = 1.0;
	float4 color = ShadingModelStandard::Shade( i, m );
	color.a = coverage;
	return color;
#endif
}

#endif
