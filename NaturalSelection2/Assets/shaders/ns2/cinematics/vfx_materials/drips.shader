//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/drips.surface_shader
//
//     float2 uvCoord = 1.0 - input.texCoord * 0.5;
//     float4 dripmask = tex2D( MaskMap, uvCoord * float2(1, 1) + float2( 0, time * 1.125 )).rgba;
//     material.emissive = dripmask.rgb * (dripmask.a * 0.1);
//
// Water running down a surface. The simplest shader in the set: one texture
// scrolling straight down, its own alpha used as a mask at a tenth strength.
//
// The UV line is odd and worth reading carefully. `1.0 - texCoord * 0.5` both
// halves the tiling and flips the coordinates, so the drip texture is mirrored
// and stretched to twice its authored size. The flip is what puts the wide end
// of the drips at the top.
//
// Only emissive is written, so this is the WriteEmissive technique alone:
// no G-buffer, no depth write, purely additive over whatever is behind.
//=============================================================================

HEADER
{
	Description = "NS2 Drips";
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
	RenderState( CullMode, NONE );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( MaskMap, Srgb, 8, "None", "_mask", ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );

	Texture2D g_tMaskMap < Channel( RGBA, Box( MaskMap ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		// Mirrored and stretched to twice the authored size - see the header.
		float2 uv = 1.0 - i.vTextureCoords.xy * 0.5;

		float4 dripmask = Tex2DS( g_tMaskMap, g_sWrap, uv + float2( 0.0, g_flTime * 1.125 ) );

		return float4( dripmask.rgb * ( dripmask.a * 0.1 ), 1.0 );
	}
}
