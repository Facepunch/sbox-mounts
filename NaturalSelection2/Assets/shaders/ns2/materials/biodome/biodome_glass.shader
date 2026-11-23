//=============================================================================
// Port of NS2 ns2/materials/biodome/biodome_glass.surface_shader
//
//     material.albedo   = float3(0, 0, 0);
//     material.tsNormal = float3(0, 0, 1);
//     material.specular = float3(2, 2, 2);
//     material.gloss    = 0.25;
//     material.emissive = float3(0.001, 0.001, 0.001);
//     material.opacity  = 1;
//
// <param name="translucent">true</param>, so see the Ns2ApplyTranslucentGlass
// notes in ns2.hlsl: there is no diffuse term, the black albedo is an unused
// output, and the pane is additive rather than alpha blended.
//
// Every input is a constant - this is just "a flat pane that reflects its
// surroundings at twice intensity". The specular of 2 clamps to white in the
// spec/gloss solver, which makes it a pure mirror-metal, and the near-zero
// emissive keeps it from disappearing completely in an unlit room.
//=============================================================================

HEADER
{
	Description = "NS2 Biodome Glass";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#ifndef S_ALPHA_TEST
	#define S_ALPHA_TEST 0
	#endif

	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 1
	#endif

	#include "common/shared.hlsl"

	#define CUSTOM_MATERIAL_INPUTS
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		return FinalizeVertex( ProcessVertex( v ) );
	}
}

PS
{
	// Depth and blend state are set explicitly below. sbox_pixel.fxc installs its
	// own translucent depth/blend state unless told not to, and a duplicate
	// RenderState does not override the earlier one - it compiles to a different
	// state entirely - so the guards are required, not just tidiness.
	//
	// S_TRANSLUCENT is also what emits BoolAttribute( translucent, true ), which
	// is what puts the surface in the translucent pass. Without it the draw
	// happens in the opaque pass, where the framebuffer copy and depth chain have
	// not been resolved yet, and sampling them just smears the colour buffer.
	#define DEPTH_STATE_ALREADY_SET 1
	#define BLEND_MODE_ALREADY_SET 1
	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 1
	#endif
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );
	RenderState( SrcBlendAlpha, ONE );
	RenderState( DstBlendAlpha, ONE );
	RenderState( BlendOpAlpha, ADD );

	float3 g_vSpecular < UiGroup( ",0/,0/0" ); Default3( 2.0, 2.0, 2.0 ); >;
	float g_flGloss < UiGroup( ",0/,0/0" ); Default1( 0.25 ); Range1( 0, 1 ); >;
	float3 g_vEmissive < UiGroup( ",0/,0/0" ); Default3( 0.001, 0.001, 0.001 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		Ns2ApplyTranslucentGlass( m, g_vSpecular, g_flGloss, g_vEmissive, 1.0 );

		m.Normal = i.vNormalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = i.vTextureCoords.xy;

		return ShadingModelStandard::Shade( i, m );
	}
}
