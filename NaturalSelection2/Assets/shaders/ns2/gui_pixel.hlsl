#define UI_SKIP_LIGHTING
#include "ui/pixel.hlsl"

RenderState( ColorWriteEnable0, RGBA );
RenderState( CullMode, NONE );
RenderState( DepthWriteEnable, false );

SamplerState g_sSparkWrap < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;
SamplerState g_sSparkClamp < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;
float g_flTextured < Attribute( "Textured" ); Default( 1 ); >;
