HEADER
{
	Description = "LightmappedGeneric";
}

FEATURES
{
	#include "source1/generic_features.hlsl"
	Feature( F_SOURCE_DECAL, 0..1, "Source 1" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#define S_SOURCE_VORTWARP 0
	#define S_SOURCE_TRANSITION 0
	#define S_SOURCE_UNLIT 0
	#include "source1/generic_common.hlsl"
}

VS
{
	#include "source1/generic_vs.hlsl"
}

PS
{
	RenderState( DepthBias, F_SOURCE_DECAL ? 250 : 0 );
	RenderState( SlopeScaleDepthBias, F_SOURCE_DECAL ? 0.5 : 0 );
	RenderState( DepthBiasClamp, F_SOURCE_DECAL ? 0.0005 : 0 );
	#include "source1/generic_ps.hlsl"
}
