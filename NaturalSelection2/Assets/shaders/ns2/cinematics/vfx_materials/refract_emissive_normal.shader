//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/refract_emissive_normal.surface_shader
//
//     float4 tex = tex2D( emissiveMap, input.texCoord +
//         (DecodeNormal( tex2D(normalMap, input.texCoord * float2(normCoord.r, normCoord.g)
//                                       + float2(time * normCoord.b, time * normCoord.a)) ).xy * defIntensity) );
//     material.emissive = tex.rgb * input.color.rgb;
//     material.opacity  = tex.a * input.color.a;
//
// Emissive with a scrolling normal map used purely as a UV distortion source,
// which is where the "refract" in the name comes from: nothing is actually
// refracted, the normal's xy just pushes the emissive lookup around.
//
// normCoord packs four unrelated values: rg is the tiling of the normal map,
// ba is its scroll speed. Materials that leave it unset get (1,1,1,1) from the
// Vec4 pre-fill, which tiles 1:1 and scrolls diagonally at 1 unit/second.
//=============================================================================

HEADER
{
	Description = "NS2 Refract Emissive Normal";
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
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );
	RenderState( SrcBlendAlpha, SRC_ALPHA );
	RenderState( DstBlendAlpha, ONE );
	RenderState( BlendOpAlpha, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( EmissiveMap, Linear, 8, "None",             "_emissive", ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flDefIntensity < UiGroup( ",0/,0/0" ); Default1( 0.01 ); Range1( 0, 1 ); >;
	float4 g_vNormCoord    < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 1.0, 1.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;

		float2 normalUv = uv * g_vNormCoord.rg + g_flTime * g_vNormCoord.ba;
		float3 distortion = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, normalUv ) );

		float4 tex = Tex2DS( g_tEmissiveMap, g_sWrap, uv + distortion.xy * g_flDefIntensity );

		float3 emissive = Ns2SrgbToLinear( tex.rgb ) * i.vColor.rgb;
		float opacity = tex.a * i.vColor.a;

		return float4( emissive, opacity );
	}
}
