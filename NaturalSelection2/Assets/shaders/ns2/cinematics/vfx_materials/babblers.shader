//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/babblers.surface_shader
//
//     const float3 tint = float3(40, 30, 2);
//
//     float4 dots  = tex2D(emissiveMap, input.texCoord*5*scaleFactor + float2( time * -.25, time * 0.3));
//     float4 dots2 = tex2D(emissiveMap, input.texCoord*scaleFactor*float2(6, 9) + float2( time * .15, time * .2)
//                  + DecodeNormal( tex2D(refractMap, input.texCoord*12 + float2(time * -0.5, time * 1)) ).xy * .1 );
//     float4 mask1 = tex2D(maskMap, input.texCoord*2*scaleFactor);
//
//     material.emissive = ((dots.rgb + mask1.rgb) * dots2.rgb) * tint;
//     material.opacity  = mask1.a + 0.2;
//
// A swarm of crawling glow specks. One texture is sampled twice at very
// different scales - 5x uniform against 6x9 anisotropic - and the second sample
// is additionally jittered by a fast-scrolling normal map, so the two layers
// beat against each other and the specks never settle into a visible tile.
//
// The multiply between the layers is what makes it read as discrete dots rather
// than a wash: a speck only lights where both layers agree.
//
// The tint is enormous (40, 30, 2) because everything feeding it is a product
// of 0..1 textures, so the result is tiny before it is scaled up. It is also
// heavily biased to red/green, giving the yellow-orange babbler glow.
//
// opacity is mask1.a + 0.2, deliberately unclamped and never below 0.2. In the
// WriteEmissive pass opacity is an additive gain, not coverage, so that floor
// keeps a faint glow everywhere the mask is empty.
//=============================================================================

HEADER
{
	Description = "NS2 Babblers";
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
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );
	RenderState( SrcBlendAlpha, SRC_ALPHA );
	RenderState( DstBlendAlpha, ONE );
	RenderState( BlendOpAlpha, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( EmissiveMap, Srgb, 8, "None", "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( MaskMap,     Srgb, 8, "None", "_mask",     ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( RefractMap,  Srgb, 8, "None", "_refract",  ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tMaskMap     < Channel( RGBA, Box( MaskMap ),     Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tRefractMap  < Channel( RGBA, Box( RefractMap ),  Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;

	float g_flScaleFactor < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		const float3 tint = float3( 40.0, 30.0, 2.0 );

		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;
		float s = g_flScaleFactor;

		float2 jitter = Ns2DecodeNormal( Tex2DS( g_tRefractMap, g_sWrap, uv * 12.0 + float2( t * -0.5, t * 1.0 ) ) ).xy * 0.1;

		float3 dots  = Tex2DS( g_tEmissiveMap, g_sWrap, uv * 5.0 * s + float2( t * -0.25, t * 0.3 ) ).rgb;
		float3 dots2 = Tex2DS( g_tEmissiveMap, g_sWrap, uv * s * float2( 6.0, 9.0 ) + float2( t * 0.15, t * 0.2 ) + jitter ).rgb;
		float4 mask1 = Tex2DS( g_tMaskMap, g_sWrap, uv * 2.0 * s );

		float3 emissive = ( ( dots + mask1.rgb ) * dots2 ) * tint;
		float opacity = mask1.a + 0.2;

		return float4( emissive, opacity );
	}
}
