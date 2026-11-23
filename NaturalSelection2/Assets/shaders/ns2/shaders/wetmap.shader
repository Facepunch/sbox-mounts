//=============================================================================
// Port of NS2 core/shaders/Wetmap.surface_shader
//
//     material.wetness = tex2D(wetnessMap, input.texCoord) * wetnessMult;
//
// This is not a visible surface. It writes only material.wetness, which Spark
// consumes in Deferred.hlsl's WriteWetnessPS:
//
//     float result = material.wetness * material.opacity;
//     float id = GetId( input.texCoord );
//     clip( float2(result - 0.0001f, 0.49 - id) );   // skip zero wetness, skip players
//     return result;
//
// The result lands in a dedicated wetness stencil that later passes read to
// darken albedo and sharpen gloss on wet ground. It never contributes albedo,
// normal, specular or emissive, so in the main G-buffer these surfaces are
// completely absent.
//
// s&box has no equivalent wetness buffer, so the faithful behaviour is to
// render nothing at all. Shading it normally would paint black patches over
// map geometry, which is strictly worse than the feature simply being missing.
// The inputs are still declared so the material parses and round-trips, and so
// this can be wired up if a wetness pass ever exists.
//=============================================================================

HEADER
{
	Description = "NS2 Wetmap";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
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
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vColor : COLOR0;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		return FinalizeVertex( i );
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
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
	RenderState( BlendOp, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( WetnessMap, Linear, 8, "None", "_wetness", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tWetnessMap < Channel( RGBA, Box( WetnessMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flWetnessMult < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 8 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float wetness = Tex2DS( g_tWetnessMap, g_sWrap, i.vTextureCoords.xy ).r * g_flWetnessMult;

		clip( -1 );

		return float4( 0.0, 0.0, 0.0, wetness * 0.0 );
	}
}
