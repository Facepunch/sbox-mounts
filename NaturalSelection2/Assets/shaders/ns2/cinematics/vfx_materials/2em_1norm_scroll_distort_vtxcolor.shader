//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/2em_1norm_scroll_distort_vtxcolor.surface_shader
//
//     float2 offset = float2(time, 0);
//     float3 tsNormal = normCoord.r * DecodeNormal( tex2D(normalMap, input.texCoord * 2 + offset * normCoord.b)) +
//                       normCoord.g * DecodeNormal( tex2D(normalMap, input.texCoord + offset.yx * normCoord.a) );
//     float2 vsNormal;
//     vsNormal.x = tsNormal.x * input.vsTangent;
//     vsNormal.y = tsNormal.y * input.vsBinormal;
//     float4 emiss1 = tex2D(emissiveMap1, input.texCoord * em1Coord.rg + float2(time * em1Coord.b, time * em1Coord.a));
//     float4 emiss2 = tex2D(emissiveMap2, input.texCoord * em2Coord.rg + float2(time * em2Coord.b, time * em2Coord.a));
//     material.emissive = ((emiss1.rgb * input.color.r) + (emiss2.rgb * input.color.g)) * tint;
//     material.transmissionColor = transmissionColor;
//     material.ssDistortion = vsNormal.xy * input.color.b * normIntensity;
//
// The workhorse of the cinematic VFX set. Two independently scrolling emissive
// layers plus a distorting normal built from two crossed samples of one map.
//
// Vertex colour is used as three separate control channels rather than a tint:
// R fades the first emissive layer, G the second, B the amount of refraction.
// That is how a single material drives very different looking effects - the
// cinematic system animates the vertex colours over the life of the effect.
//
// The xyzw of each *Coord input is (tile U, tile V, scroll U, scroll V), and
// normCoord is (weight 1, weight 2, scroll 1, scroll 2) with the first sample
// tiled 2x and scrolling in U while the second is tiled 1x and scrolls in V.
//
// This writes emissive, transmission and refraction but never albedo, specular
// or gloss, so Spark builds no WriteDeferred technique for it: the surface does
// not enter the G-buffer and does not occlude. It warps what is behind it and
// adds its glow on top.
//=============================================================================

HEADER
{
	Description = "NS2 2 Emissive 1 Normal Scroll Distort VtxColor";
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
	#include "ns2/ns2_refract.hlsl"

	BoolAttribute( bWantsFBCopyTexture, true );

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, NONE );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ZERO );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( EmissiveMap1, Srgb,   8, "None",             "_emissive1", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( EmissiveMap2, Srgb,   8, "None",             "_emissive2", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NormalMap,    Linear, 8, "NormalizeNormals", "_normal",    ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );

	Texture2D g_tEmissiveMap1 < Channel( RGBA, Box( EmissiveMap1 ), Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tEmissiveMap2 < Channel( RGBA, Box( EmissiveMap2 ), Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tNormalMap    < Channel( RGBA, Box( NormalMap ),    Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float3 g_vTransmissionColor < UiGroup( ",0/,0/0" ); Default3( 1.0, 1.0, 1.0 ); >;
	float3 g_vTint < UiGroup( ",0/,0/0" ); Default3( 1.0, 1.0, 1.0 ); >;
	float4 g_vEm1Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vEm2Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vNormCoord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float g_flNormIntensity < UiGroup( ",0/,0/0" ); Default1( 0.10 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float2 offset = float2( g_flTime, 0.0 );

		float3 tsNormal =
			g_vNormCoord.r * Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv * 2.0 + offset * g_vNormCoord.b ) ) +
			g_vNormCoord.g * Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv + offset.yx * g_vNormCoord.a ) );

		float2 distortion = Ns2DistortionToScreen( tsNormal, i.vTangentUWs.xyz, i.vTangentVWs.xyz );
		distortion *= i.vColor.b * g_flNormIntensity;

		float3 emiss1 = Tex2DS( g_tEmissiveMap1, g_sWrap, uv * g_vEm1Coord.rg + g_flTime * g_vEm1Coord.ba ).rgb;
		float3 emiss2 = Tex2DS( g_tEmissiveMap2, g_sWrap, uv * g_vEm2Coord.rg + g_flTime * g_vEm2Coord.ba ).rgb;

		float3 emissive = ( emiss1 * i.vColor.r + emiss2 * i.vColor.g ) * g_vTint;

		float3 scene = Ns2Refract( i.vPositionSs.xy, i.vPositionSs.z, distortion, g_vTransmissionColor, 1.0 );

		return float4( scene + emissive, 1.0 );
	}
}
