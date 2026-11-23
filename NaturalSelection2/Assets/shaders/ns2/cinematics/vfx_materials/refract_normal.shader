//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/refract_normal.surface_shader
//
//     material.transmissionColor = float3(1,1,1) * input.color.rgb;
//     material.ssDistortion = DecodeNormal( tex2D(normalMap, input.texCoord) ).xy
//                           * 1 * input.color.a * tex2D(normalMap, input.texCoord).a;
//
// A refraction-only effect driven entirely by vertex colour, which is how the
// cinematic system animates it: RGB tints what shows through, alpha fades the
// bend out. The normal map's own alpha channel masks the effect further, so
// the artist can carve the distorting region out of the sheet.
//
// Note there is no 0.01 style scale here - the raw decoded normal is used, so
// this bends far harder than glass_refract and is meant for short bursts.
//=============================================================================

HEADER
{
	Description = "NS2 Refract Normal";
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
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ZERO );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( NormalMap, Linear, 8, "None", "_normal", ",0/,0/0", Default4( 0.5, 0.5, 1.0, 1.0 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;

		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float3 tsNormal = Ns2DecodeNormal( normal ) * i.vColor.a * normal.a;

		float2 distortion = Ns2DistortionToScreen( tsNormal, i.vTangentUWs.xyz, i.vTangentVWs.xyz );

		return float4( Ns2Refract( i.vPositionSs.xy, i.vPositionSs.z, distortion, i.vColor.rgb, 1.0 ), 1.0 );
	}
}
