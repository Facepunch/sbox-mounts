HEADER
{
	Description = "NS2 kindatomuch exo shield";
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
	#include "ns2/ns2_refract.hlsl"

	BoolAttribute( bWantsFBCopyTexture, true );

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ZERO );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap, Linear, 8, "None", "_albedomap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tAlbedoMap < Channel( RGBA, Box( AlbedoMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( OpacityMap, Linear, 8, "None", "_opacitymap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tOpacityMap < Channel( RGBA, Box( OpacityMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 normal = normalize( i.vNormalWs );
		float3 view = normalize( g_vCameraPositionWs - ( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz ) );
		float fresnel = pow( 1 - saturate( dot( view, normal ) ), 3 );
		float3 transmission = i.vColor.rgb * ( 1 + fresnel - 1.5 * float3( 0.60, 0.45, 0.42 ) );
		transmission += 16 * Tex2DS( g_tOpacityMap, g_sWrap, i.vTextureCoords.xy ).rgb;
		float2 distortion = Vector3WsToVs( normal ).xy * float2( -0.0004, 0.0004 );
		Material m = Material::Init();
		Ns2ApplyTranslucentGlass( m, 7 * fresnel + 0.15, 2 * fresnel + 1.75, 0, 0.5 );
		m.Normal = normal;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = i.vTextureCoords.xy;
		m.Emission += Ns2Refract( i.vPositionSs.xy, i.vPositionSs.z, distortion, transmission, 0.5 );
		return ShadingModelStandard::Shade( i, m );
	}
}
