//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/waterfall.surface_shader
//
//     float3 DecodeNormal(const float4 texNormal)
//     {
//         float4 n = texNormal * 2 - 1;
//         //return float3(n.a, n.g, sqrt(1 - dot(n.ag, n.ag)) );
//         return n.xyz;
//     }
//
//     const float3 tint = float3(.5, .55, .65);
//
//     bordermask = MaskMap     ( uv * (1, 2)     + (0,          time * -1.1 ) )
//     bubbles    = EmissiveMap ( uv * (11, 35)   + (time * .08, time * -3.93) )
//     bubbles2   = EmissiveMap ( uv * (5, 13.2)  + (time * -.1, time * -3.3 ) )
//     albedo1    = albedoMap   ( uv * (4, 8.1)   + (time * -.02, time * -1.85) )
//     albedo2    = albedoMap   ( uv * (3, 6.37)  + (time * .5,  time * -1.84) )
//     noise      = albedoMap   ( uv * (1.3, .8)  + (time * -.02, time * -.5 ) )
//     noise2     = albedoMap   ( uv * (2.37, 2.53) + (0,        time * -.82 ) )
//     normal1    = DecodeNormal( normalMap( uv * (4, 8.1) + (time * -.1, time * -1.85) ) )
//
//     material.emissive = ((albedo1 + albedo2 + noise)) * tint + (bubbles*bubbles2) * 100;
//     material.albedo   = ((albedo1 + albedo2));
//     material.opacity  = ((noise + noise2)) * bordermask;
//     material.tsNormal = (normal1);
//
// Unlike the lava family this writes ALBEDO, so it does get a G-buffer pass:
// it is a lit, depth-writing, alpha-tested surface with an emissive term on
// top, not a pure additive overlay. That is the whole reason it is built on
// the level_emissive template rather than the emissive_glow one.
//
// Two details worth keeping:
//
//   - DecodeNormal here is NOT Spark's usual one. The normal reconstruction
//     line (float3(n.a, n.g, sqrt(...))) is commented out in the original and
//     replaced with a plain n.xyz, so the normal map is read as a straight
//     RGB tangent normal. Ns2DecodeNormal must not be used - it would swizzle.
//
//   - (bubbles * bubbles2) * 100 is the froth. Both maps tile very densely
//     along V (35 and 13.2) and scroll at nearly 4 units/second, so their
//     product is mostly black with sparse bright coincidences; the x100 turns
//     those into blown-out specks of spray.
//
// bordermask scrolls the alpha, which combined with alphaTest is what eats the
// top and bottom of the sheet so it does not end in a hard edge.
//
// fresnel is computed and never used in the original, as in the rest of the
// vfx family. Omitted.
//
// The original declares normalMap srgb="true", which is unusual for a normal
// map but is what Spark does, so the sRGB decode is applied here too rather
// than silently "fixing" it.
//=============================================================================

HEADER
{
	Description = "NS2 Waterfall";
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
	#define S_ALPHA_TEST 1
	#endif

	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 0
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
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	// twoSided in the original.
	RenderState( CullMode, NONE );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap,   Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( MaskMap,     Linear, 8, "None",             "_mask",     ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tMaskMap     < Channel( RGBA, Box( MaskMap ),     Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		const float3 tint = float3( 0.5, 0.55, 0.65 );

		float3 bordermask = Ns2SrgbToLinear( Tex2DS( g_tMaskMap,     g_sWrap, uv * float2( 1.0,  2.0  ) + float2( 0.0,      t * -1.1  ) ).rgb );
		float3 bubbles    = Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, uv * float2( 11.0, 35.0 ) + float2( t *  0.08, t * -3.93 ) ).rgb );
		float3 bubbles2   = Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, uv * float2( 5.0,  13.2 ) + float2( t * -0.1,  t * -3.3  ) ).rgb );
		float3 albedo1    = Ns2SrgbToLinear( Tex2DS( g_tAlbedoMap,   g_sWrap, uv * float2( 4.0,  8.1  ) + float2( t * -0.02, t * -1.85 ) ).rgb );
		float3 albedo2    = Ns2SrgbToLinear( Tex2DS( g_tAlbedoMap,   g_sWrap, uv * float2( 3.0,  6.37 ) + float2( t *  0.5,  t * -1.84 ) ).rgb );
		float3 noise      = Ns2SrgbToLinear( Tex2DS( g_tAlbedoMap,   g_sWrap, uv * float2( 1.3,  0.8  ) + float2( t * -0.02, t * -0.5  ) ).rgb );
		float3 noise2     = Ns2SrgbToLinear( Tex2DS( g_tAlbedoMap,   g_sWrap, uv * float2( 2.37, 2.53 ) + float2( 0.0,      t * -0.82 ) ).rgb );

		// Plain RGB decode - the original's DecodeNormal returns n.xyz, the
		// swizzled reconstruction is commented out. Do not use Ns2DecodeNormal.
		float3 normalTex = Ns2SrgbToLinear( Tex2DS( g_tNormalMap, g_sWrap, uv * float2( 4.0, 8.1 ) + float2( t * -0.1, t * -1.85 ) ).rgb );
		float3 normal1 = normalTex * 2.0 - 1.0;

		float3 emissive = ( albedo1 + albedo2 + noise ) * tint + ( bubbles * bubbles2 ) * 100.0;
		float3 albedo = albedo1 + albedo2;

		// Spark assigns a float3 to material.opacity, which truncates to .x.
		float opacity = ( ( noise.x + noise2.x ) ) * bordermask.x;

		Material m = Material::Init();

		// The original writes no specular or gloss, so Spark leaves both at zero.
		Ns2ApplySpecularGloss( m, albedo, float3( 0.0, 0.0, 0.0 ), 0.0 );

		m.Emission = emissive;
		m.Opacity = opacity;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = TransformNormal( normal1, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
