//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/sprinklers.surface_shader
//
//     float4 emiss1 = tex2D(emissiveMap1, input.texCoord * em1Coord.rg + time * em1Coord.ba + jitter);
//     float4 emiss2 = tex2D(emissiveMap2, input.texCoord * em2Coord.rg + time * em2Coord.ba + jitter);
//     float fresnel = pow(max(-input.vsNormal.z, 0), fresnelPower);
//
//     material.emissive = (emiss1.rgb * emiss2.rgb * input.color.rgb) * tint * fresnel;
//
// A fine water spray. This is the 2em_1norm_scroll family again, but with two
// differences that change how it reads:
//
//  - BOTH emissive layers share one jitter sample, where 2em_1norm_scroll gives
//    each its own. So the two layers wobble together as one sheet rather than
//    shearing against each other.
//  - the fresnel term, pow(N dot V, fresnelPower), is taken un-inverted. The
//    spray is brightest where it faces the camera and vanishes edge-on, which
//    is what stops the flat cards giving themselves away at grazing angles.
//
// The two layers are MULTIPLIED, so a droplet only lights where both agree -
// that is what breaks a continuous sheet into spray.
//
// Only emissive is written, so this is the WriteEmissive technique alone:
// no G-buffer, no depth write, purely additive.
//=============================================================================

HEADER
{
	Description = "NS2 Sprinklers";
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
		PixelInput o = ProcessVertex( v );
		o.vColor = v.vColor;
		return FinalizeVertex( o );
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
	float g_flNormIntensity < UiGroup( ",0/,0/0" ); Default1( 0.10 ); Range1( 0, 1 ); >;
	float g_flFresnelPower < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		// One shared jitter sample drives both layers.
		float2 duv = uv * g_vNormCoord.rg + t * g_vNormCoord.ba;
		float2 jitter = Ns2DecodeNormal( Tex2DS( g_tRefractMap, g_sWrap, duv ) ).xy * g_flNormIntensity;

		float3 emiss1 = Tex2DS( g_tEmissiveMap1, g_sWrap, uv * g_vEm1Coord.rg + t * g_vEm1Coord.ba + jitter ).rgb;
		float3 emiss2 = Tex2DS( g_tEmissiveMap2, g_sWrap, uv * g_vEm2Coord.rg + t * g_vEm2Coord.ba + jitter ).rgb;

		// max(-vsNormal.z, 0) in Spark's view space is N dot V, un-inverted here.
		float nDotV = saturate( dot( normalize( i.vNormalWs ), normalize( -i.vPositionWithOffsetWs.xyz ) ) );
		float fresnel = pow( nDotV, g_flFresnelPower );

		float3 emissive = ( emiss1 * emiss2 * i.vColor.rgb ) * g_vTint * fresnel;

		return float4( emissive, 1.0 );
	}
}
