//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/descent_gravity.surface_shader
//
//     float4 mask = tex2D(maskMap, input.texCoord);
//     float4 tex3 = tex2D(cloudMap, input.texCoord*2 + float2(time * -.1, time * 0.5));
//     float4 tex2 = tex2D(emissiveMap, input.texCoord + float2(time * -.02, time * -.24));
//     float4 tex  = tex2D(emissiveMap, input.texCoord*1.5 + float2(time * .05, time * -1)
//                 + DecodeNormal( tex2D(normalMap, input.texCoord*.5 + float2(time * -0.1, time * 0.1)) ).xy * .05 );
//
//     material.emissive = tex.rgb + tex2.rgb * 80 * emissiveMod;
//     material.opacity  = tex.rgb * tex3.a * tex3.rgb * mask.rgb * 6 * emissiveMod;
//
// The gravity shaft in ns2_descent. Two emissive layers scroll downward at very
// different rates - a fast streaked layer and a slow one - which reads as
// material falling at speed past a slower background.
//
// The precedence in the emissive line matters and is not a typo: only tex2 is
// multiplied by 80 and by emissiveMod, tex is added raw. So the slow layer is
// the blazing one and the fast layer rides over it as faint streaking.
//
// The opacity line is a five-way product, and remember that opacity in the
// WriteEmissive pass is an unclamped additive gain rather than coverage. tex3's
// own alpha AND its rgb both appear, and mask confines the whole thing to the
// shaft, so the brightness gain is concentrated in a small region.
//
// Both assignments truncate to .x - Spark drops the extra components when a
// float3 is assigned to a float.
//=============================================================================

HEADER
{
	Description = "NS2 Descent Gravity";
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
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );
	RenderState( SrcBlendAlpha, SRC_ALPHA );
	RenderState( DstBlendAlpha, ONE );
	RenderState( BlendOpAlpha, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( EmissiveMap, Srgb,   8, "None", "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( CloudMap,    Srgb,   8, "None", "_cloud",    ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( MaskMap,     Srgb,   8, "None", "_mask",     ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "None", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tCloudMap    < Channel( RGBA, Box( CloudMap ),    Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tMaskMap     < Channel( RGBA, Box( MaskMap ),     Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flEmissiveMod < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;
		float mod = g_flEmissiveMod;

		float2 jitter = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv * 0.5 + float2( t * -0.1, t * 0.1 ) ) ).xy * 0.05;

		float4 mask = Tex2DS( g_tMaskMap, g_sWrap, uv );
		float4 tex3 = Tex2DS( g_tCloudMap, g_sWrap, uv * 2.0 + float2( t * -0.1, t * 0.5 ) );
		float4 tex2 = Tex2DS( g_tEmissiveMap, g_sWrap, uv + float2( t * -0.02, t * -0.24 ) );
		float4 tex  = Tex2DS( g_tEmissiveMap, g_sWrap, uv * 1.5 + float2( t * 0.05, t * -1.0 ) + jitter );

		// Only tex2 takes the x80 and the modifier - see the header.
		float3 emissive = tex.rgb + tex2.rgb * 80.0 * mod;
		float3 opacity = tex.rgb * tex3.a * tex3.rgb * mask.rgb * 6.0 * mod;

		// Spark truncates the float3 assigned to opacity to .x.
		return float4( emissive, opacity.x );
	}
}
