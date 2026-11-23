//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/2em_1mask_1norm_scroll_refract_tint.surface_shader
//
//     float4 emiss1 = tex2D(emissiveMap1, input.texCoord * em1Coord.rg + float2(time * em1Coord.b, time * em1Coord.a)
//                   + DecodeNormal( tex2D(refractMap, input.texCoord * norm2Coord.rg + float2(time * norm2Coord.b, time * norm2Coord.a)) ).xy * normIntensity );
//     float4 emiss2 = tex2D(emissiveMap2, input.texCoord * em2Coord.rg + float2(time * em2Coord.b, time * em2Coord.a)
//                   + DecodeNormal( tex2D(refractMap, input.texCoord * normCoord.rg + float2(time * normCoord.b, time * normCoord.a)) ).xy * normIntensity );
//     float4 mask1 = tex2D(maskMap, input.texCoord * maskCoord.rg + float2(time * maskCoord.b, time * maskCoord.a));
//
//     if (addBlending == 1)
//         material.emissive = (emiss1.rgb + emiss2.rgb) * mask1.rgb * tint * intensity * emissiveMod;
//     else
//         material.emissive = (emiss1.rgb * emiss2.rgb) * mask1.rgb * tint * intensity * emissiveMod;
//
//     material.opacity = mask1.rgb * emissiveMod;
//
// 2em_1norm_scroll_refract_tint with a third scrolling layer used as a mask.
// As there, "refract" is a UV perturbation, not screen-space refraction.
//
// Two details are preserved deliberately rather than tidied up:
//
//  - addBlending is compared against exactly 1 here, where the sibling shader
//    lerps by it. So intermediate values behave differently between the two:
//    anything that is not exactly 1 multiplies. Written as a step() to keep
//    that behaviour without a branch.
//  - "material.opacity = mask1.rgb" assigns a float3 to a float, which HLSL
//    truncates to .r. The mask's red channel is therefore the opacity while
//    all three channels tint the emissive.
//
// Only the emissive and opacity groups are written, so there is no
// WriteDeferred technique and the surface is additive and non-occluding, with
// opacity acting as the source alpha of the additive blend.
//=============================================================================

HEADER
{
	Description = "NS2 2 Emissive 1 Mask 1 Normal Scroll Refract Tint";
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
	RenderState( CullMode, NONE );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( EmissiveMap1, Srgb, 8, "None", "_emissive1", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( EmissiveMap2, Srgb, 8, "None", "_emissive2", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( MaskMap,      Srgb, 8, "None", "_mask",      ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( RefractMap,   Srgb, 8, "None", "_refract",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );

	Texture2D g_tEmissiveMap1 < Channel( RGBA, Box( EmissiveMap1 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tEmissiveMap2 < Channel( RGBA, Box( EmissiveMap2 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tMaskMap      < Channel( RGBA, Box( MaskMap ),      Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tRefractMap   < Channel( RGBA, Box( RefractMap ),   Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;

	float3 g_vTint < UiGroup( ",0/,0/0" ); Default3( 1.0, 1.0, 1.0 ); >;
	float4 g_vEm1Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vEm2Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vMaskCoord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vNormCoord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vNorm2Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float g_flNormIntensity < UiGroup( ",0/,0/0" ); Default1( 0.10 ); Range1( 0, 1 ); >;
	float g_flAddBlending < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flIntensity < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;
	float g_flEmissiveMod < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;

	float2 ScrollDistort( float2 uv, float4 coord )
	{
		float2 duv = uv * coord.rg + g_flTime * coord.ba;
		return Ns2DecodeNormal( Tex2DS( g_tRefractMap, g_sWrap, duv ) ).xy * g_flNormIntensity;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;

		float3 emiss1 = Tex2DS( g_tEmissiveMap1, g_sWrap, uv * g_vEm1Coord.rg + g_flTime * g_vEm1Coord.ba + ScrollDistort( uv, g_vNorm2Coord ) ).rgb;
		float3 emiss2 = Tex2DS( g_tEmissiveMap2, g_sWrap, uv * g_vEm2Coord.rg + g_flTime * g_vEm2Coord.ba + ScrollDistort( uv, g_vNormCoord ) ).rgb;
		float3 mask1 = Tex2DS( g_tMaskMap, g_sWrap, uv * g_vMaskCoord.rg + g_flTime * g_vMaskCoord.ba ).rgb;

		float bAdd = step( 1.0, g_flAddBlending );
		float3 layers = lerp( emiss1 * emiss2, emiss1 + emiss2, bAdd );

		float3 emissive = layers * mask1 * g_vTint * g_flIntensity * g_flEmissiveMod;
		float opacity = mask1.r * g_flEmissiveMod;

		return float4( emissive * opacity, 1.0 );
	}
}
