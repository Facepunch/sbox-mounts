//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/window_rain.surface_shader
//
//     float2 offset1 = float2(norm1Coord.b * time, norm1Coord.a * time);
//     float2 offset2 = float2(norm2Coord.b * time, norm2Coord.a * time);
//
//     float3 tsNormal = DecodeNormal( tex2D(normalMap, input.texCoord * norm1Coord.rg + offset1) ) -
//                       DecodeNormal( tex2D(normalMap, input.texCoord * norm2Coord.rg + offset2) );
//
//     float2 vsNormal;
//     vsNormal.x = tsNormal.x * input.vsTangent;
//     vsNormal.y = tsNormal.y * input.vsBinormal;
//
//     float4 emiss1 = tex2D(emissiveMap1, input.texCoord * em1Coord.rg + float2(time * em1Coord.b, time * em1Coord.a));
//     float4 emiss2 = tex2D(emissiveMap2, input.texCoord * em2Coord.rg + float2(time * em2Coord.b, time * em2Coord.a));
//
//     material.emissive = ((emiss1.rgb * input.color.r) * (emiss2.rgb * input.color.g)) * tint;
//     material.transmissionColor = transmissionColor;
//     material.ssDistortion = vsNormal.xy * input.color.b * normIntensity;
//
// Rain running down a window. This is the main menu backdrop's glass.
//
// Two differences from its close sibling 2em_1norm_scroll_distort_vtxcolor, and
// both matter:
//
//  - the two normal samples are SUBTRACTED rather than weighted and summed.
//    Subtracting two copies of the same map at different tiling and scroll
//    speeds cancels the flat areas to zero and leaves only where the two
//    disagree, which is what makes the streaks read as rivulets crawling over
//    each other instead of a single sliding sheet. It also means a still frame
//    is nearly undistorted - the effect lives entirely in the motion.
//  - the two emissive layers are MULTIPLIED rather than added, so the highlight
//    only appears where both layers are lit. That keeps the specular glints
//    sparse and stops the window washing out.
//
// Each normal sample gets its own tiling and scroll from norm1Coord/norm2Coord
// (tile U, tile V, scroll U, scroll V), unlike the sibling which hardcodes a
// 2x tile on the first sample.
//
// Vertex colour is again three separate controls: R and G fade the two emissive
// layers, B scales the refraction.
//
// Writes emissive, transmission and refraction but never albedo/specular/gloss,
// so Spark builds no WriteDeferred technique: no G-buffer, no depth, no
// occlusion. It warps what is behind it and adds its glints on top.
//=============================================================================

HEADER
{
	Description = "NS2 Window Rain";
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
	// S_TRANSLUCENT is what emits BoolAttribute( translucent, true ) in
	// sbox_pixel.fxc, which is what puts this in the translucent pass. Without
	// it the surface draws in the opaque pass, where the framebuffer copy has
	// not been taken yet, and sampling it just smears the colour buffer.
	//
	// The blend and depth state are set by hand below, so tell sbox_pixel.fxc
	// not to install the default translucent ones over the top.
	#define DEPTH_STATE_ALREADY_SET 1
	#define BLEND_MODE_ALREADY_SET 1
	#define S_TRANSLUCENT 1

	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"
	#include "ns2/ns2_refract.hlsl"

	BoolAttribute( bWantsFBCopyTexture, true );

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ZERO );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( EmissiveMap1, Srgb, 8, "None", "_emissive1", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( EmissiveMap2, Srgb, 8, "None", "_emissive2", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NormalMap,    Srgb, 8, "None", "_normal",    ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );

	Texture2D g_tEmissiveMap1 < Channel( RGBA, Box( EmissiveMap1 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tEmissiveMap2 < Channel( RGBA, Box( EmissiveMap2 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tNormalMap    < Channel( RGBA, Box( NormalMap ),    Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;

	float3 g_vTransmissionColor < UiGroup( ",0/,0/0" ); Default3( 1.0, 1.0, 1.0 ); >;
	float3 g_vTint < UiGroup( ",0/,0/0" ); Default3( 1.0, 1.0, 1.0 ); >;
	float4 g_vEm1Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vEm2Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vNorm1Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vNorm2Coord < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float g_flNormIntensity < UiGroup( ",0/,0/0" ); Default1( 0.10 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;

		float2 offset1 = g_vNorm1Coord.ba * g_flTime;
		float2 offset2 = g_vNorm2Coord.ba * g_flTime;

		float3 tsNormal =
			Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv * g_vNorm1Coord.rg + offset1 ) ) -
			Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv * g_vNorm2Coord.rg + offset2 ) );

		float2 distortion = Ns2DistortionToScreen( tsNormal, i.vTangentUWs.xyz, i.vTangentVWs.xyz );
		distortion *= i.vColor.b * g_flNormIntensity;

		float3 emiss1 = Tex2DS( g_tEmissiveMap1, g_sWrap, uv * g_vEm1Coord.rg + g_flTime * g_vEm1Coord.ba ).rgb;
		float3 emiss2 = Tex2DS( g_tEmissiveMap2, g_sWrap, uv * g_vEm2Coord.rg + g_flTime * g_vEm2Coord.ba ).rgb;

		float3 emissive = ( emiss1 * i.vColor.r ) * ( emiss2 * i.vColor.g ) * g_vTint;

		float3 scene = Ns2Refract( i.vPositionSs.xy, i.vPositionSs.z, distortion, g_vTransmissionColor, 1.0 );

		return float4( scene + emissive, 1.0 );
	}
}
