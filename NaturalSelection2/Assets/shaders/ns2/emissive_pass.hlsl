#define DEPTH_STATE_ALREADY_SET 1
#define BLEND_MODE_ALREADY_SET 1
#define S_TRANSLUCENT 1
#include "common/pixel.hlsl"
#include "ns2/surface_inputs.hlsl"

RenderState( DepthEnable, true );
RenderState( DepthFunc, GREATER_EQUAL );
RenderState( DepthWriteEnable, false );
RenderState( BlendEnable, true );
RenderState( SrcBlend, SRC_ALPHA );
RenderState( BlendOp, ADD );
