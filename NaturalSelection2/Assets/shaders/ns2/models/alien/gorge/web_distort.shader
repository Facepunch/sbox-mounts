//=============================================================================
// Port of NS2 ns2/models/alien/gorge/web_distort.surface_shader
//
//     float distance = length(input.vsPosition);
//     float visibility = saturate((distance - fullVisDist) / (noVisDist - fullVisDist));
//     if (visibility > 0)
//     {
//         float3 tsNormal = DecodeNormal(tex2D(normalMap, input.texCoord));
//         float3 vsNormal = tsNormal.x * input.vsTangent +
//                           tsNormal.y * input.vsBinormal +
//                           tsNormal.z * input.vsNormal;
//         material.ssDistortion = vsNormal.xy * float2(1, -1) * (1 - visibility)
//                               * tex2D(opacityMap, input.texCoord).r * distortionIntensity;
//     }
//
// Gorge webs seen by the enemy team. The original comment is worth keeping in
// mind: "Can't do transparency, so try to erode the edges by cutting out
// fragments facing away from the viewer". Refraction is the only tool this
// shader has, so distance is expressed purely as how much the web bends light.
//
// Spark enables distortion only inside the distance transition.
//=============================================================================

HEADER
{
	Description = "NS2 Web Distort";
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
	#include "ns2/ns2_refract.hlsl"

	BoolAttribute( bWantsFBCopyTexture, true );

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ZERO );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( NormalMap,  Linear, 8, "NormalizeNormals", "_normal",  ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( OpacityMap, Linear, 8, "None",             "_opacity", ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tNormalMap  < Channel( RGBA, Box( NormalMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tOpacityMap < Channel( RGBA, Box( OpacityMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	// Keep material overrides in the authored units.
	float g_flNoVisDist < UiGroup( ",0/,0/0" ); Default1( 5.0 ); Range1( 0, 1024 ); >;
	float g_flFullVisDist < UiGroup( ",0/,0/0" ); Default1( 2.5 ); Range1( 0, 1024 ); >;
	float g_flDistortionIntensity < UiGroup( ",0/,0/0" ); Default1( 0.25 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;

		float dist = distance( i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz, g_vCameraPositionWs ) / NS2_METRE;
		float visibility = saturate( ( dist - g_flFullVisDist ) / ( g_flNoVisDist - g_flFullVisDist ) );

		if ( visibility <= 0 || visibility >= 1 )
			return float4( Ns2SampleScene( i.vPositionSs.xy ), 1 );

		float3 tsNormal = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv ) );

		float3 wsPerturb =
			tsNormal.x * i.vTangentUWs.xyz +
			tsNormal.y * i.vTangentVWs.xyz +
			tsNormal.z * i.vNormalWs.xyz;

		float3 vsPerturb = Vector3WsToVs( wsPerturb );

		float opacity = Tex2DS( g_tOpacityMap, g_sWrap, uv ).r;

		float2 distortion = float2( vsPerturb.x, -vsPerturb.y ) * ( 1.0 - visibility ) * opacity * g_flDistortionIntensity;

		return float4( Ns2Refract( i.vPositionSs.xy, i.vPositionSs.z, distortion, float3( 1.0, 1.0, 1.0 ), 1.0 ), 1.0 );
	}
}
