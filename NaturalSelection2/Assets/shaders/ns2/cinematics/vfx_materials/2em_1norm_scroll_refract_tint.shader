//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/2em_1norm_scroll_refract_tint.surface_shader
//
//     float4 emiss1 = tex2D(emissiveMap1, input.texCoord * em1Coord.rg + float2(time * em1Coord.b, time * em1Coord.a)
//                   + DecodeNormal( tex2D(refractMap, input.texCoord * norm2Coord.rg + float2(time * norm2Coord.b, time * norm2Coord.a)) ).xy * normIntensity );
//     float4 emiss2 = tex2D(emissiveMap2, input.texCoord * em2Coord.rg + float2(time * em2Coord.b, time * em2Coord.a)
//                   + DecodeNormal( tex2D(refractMap, input.texCoord * normCoord.rg + float2(time * normCoord.b, time * normCoord.a)) ).xy * normIntensity );
//     material.emissive = lerp( emiss1.rgb * emiss2.rgb, emiss1.rgb + emiss2.rgb, addBlending ) * tint * intensity * input.color.rgb;
//
// Despite the name this shader does NOT refract. Nothing is written to
// ssDistortion and the scene behind is never touched. "refract" here means the
// UVs of the two emissive layers are perturbed by a scrolling normal map, so
// the fire/energy sheets wobble against each other. It is a texture-space
// effect, not a screen-space one, and each emissive layer gets its own
// independent perturbation (norm2Coord drives the first, normCoord the second).
//
// addBlending is a 0/1 float, not a bool, and it is used as a lerp factor
// between multiplying the two layers and adding them. Multiply keeps the effect
// confined to where both layers overlap; add lets either layer show alone.
// Values in between are legal and some materials do use them.
//
// The only output group written is emissive, so Spark generates only the
// WriteEmissive technique: no G-buffer, no depth, no occlusion. The result is
// purely additive over the scene.
//=============================================================================

HEADER
{
	Description = "NS2 2 Emissive 1 Normal Scroll Refract Tint";
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
		PixelInput o = ProcessVertex( v );
		o.vColor = v.vColor;
		return FinalizeVertex( o );
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
	CreateInputTexture2D( RefractMap,   Srgb, 8, "None", "_refract",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );

	Texture2D g_tEmissiveMap1 < Channel( RGBA, Box( EmissiveMap1 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tEmissiveMap2 < Channel( RGBA, Box( EmissiveMap2 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tRefractMap   < Channel( RGBA, Box( RefractMap ),   Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;

	float3 g_vTint < UiGroup( ",0/,0/0" ); Default3( 1.0, 1.0, 1.0 ); >;
	float4 g_vEm1Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vEm2Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vNormCoord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vNorm2Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float g_flNormIntensity < UiGroup( ",0/,0/0" ); Default1( 0.10 ); Range1( 0, 1 ); >;
	float g_flAddBlending < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flIntensity < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;

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

		float3 emissive = lerp( emiss1 * emiss2, emiss1 + emiss2, g_flAddBlending ) * g_vTint * g_flIntensity * i.vColor.rgb;

		return float4( emissive, 1.0 );
	}
}
