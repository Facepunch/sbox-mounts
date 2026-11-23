//=============================================================================
// Port of NS2 ns2/models/props/derelict/derelict_glass.surface_shader
//
//     material.albedo   = float3(0, 0, 0);
//     material.tsNormal = DecodeNormal( tex2D(normalMap, input.texCoord) );
//     material.specular = float3(0.5, 0.5, 0.5);
//     material.gloss    = 0.1;
//     material.emissive = tex2D(emissiveMap, input.texCoord).rgb;
//     material.opacity  = tex2D(opacityMap, input.texCoord).r;
//
// <param name="translucent">true</param>, so this is additive - see the
// Ns2ApplyTranslucentGlass notes in ns2.hlsl.
//
// The opacity map only scales the emissive here. Spark feeds opacity into the
// transmission multiply as lerp( 1, transmissionColor, opacity ), and this
// shader never sets transmissionColor, so that multiply stays at 1 and the
// map has no other effect. It reads as "where the glass glows", not "where
// the glass is solid".
//
// seasonal/halloween2014/models/webs/webs.surface_shader is byte for byte the
// same shader and is ported separately so the material paths keep resolving.
//=============================================================================

HEADER
{
	Description = "NS2 Derelict Glass";
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

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( OpacityMap,  Linear, 8, "None",             "_opacity",  ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tOpacityMap  < Channel( RGBA, Box( OpacityMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float3 g_vSpecular < UiGroup( ",0/,0/0" ); Default3( 0.5, 0.5, 0.5 ); >;
	float g_flGloss < UiGroup( ",0/,0/0" ); Default1( 0.1 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		float3 emissive = SrgbGammaToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, uv ).rgb );
		float opacity = Tex2DS( g_tOpacityMap, g_sWrap, uv ).r;

		Ns2ApplyTranslucentGlass( m, g_vSpecular, g_flGloss, emissive, opacity );

		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		m.Normal = TransformNormal( Ns2DecodeNormal( normal ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
