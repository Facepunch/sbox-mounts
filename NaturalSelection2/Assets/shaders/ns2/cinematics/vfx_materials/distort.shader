HEADER
{
	Description = "NS2 Distort";
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

	CreateInputTexture2D( NormalMap, Linear, 8, "NormalizeNormals", "_normal", ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissive", ",0/,0/0", Default3( 0, 0, 0 ) );
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flMaxRange < Default1( 0 ); >;
	float g_flDistortAmount < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 view = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs;
		float3 up = g_vCameraUpDirWs;
		float3 forward = g_vCameraDirWs;
		float3 right = cross( forward, up );
		float2 lateral = float2( dot( right, view ), dot( up, view ) ) / NS2_METRE;
		float depth = dot( forward, view ) / NS2_METRE;
		float fade = g_flMaxRange == 0 ? 0 : smoothstep( 1, 0.5, 0.33 * length( lateral ) ) * ( 1 - saturate( ( depth - 2 ) / g_flMaxRange ) );
		float intensity = 1 - g_flDistortAmount;
		if ( intensity + fade <= 0 )
			return float4( Ns2SampleScene( i.vPositionSs.xy ), 1 );

		float2 tangent = float2( dot( right, i.vTangentUWs ), dot( up, i.vTangentUWs ) );
		float wave = 0.5 * intensity * cos( 2 * g_flTime + 3 * tangent.x );
		float limit = ( fade + intensity ) * 0.03;
		float normalX = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy ) ).x;
		// Spark evaluates the binormal expression after a terminating semicolon.
		float2 distortion = clamp( tangent * wave, -limit, limit ) * normalX;
		float z = dot( normalize( i.vNormalWs ), forward );
		float fresnel = pow( saturate( -z ), 1.5 * ( 1.8 - intensity ) );
		float3 transmission = max( 0.25, 1 - fade * 0.8 - 1.4 * intensity * fresnel ) * i.vColor.rgb;
		float3 glow = saturate( Tex2DS( g_tEmissiveMap, g_sWrap, i.vTextureCoords.xy ).rgb );
		float edge = -0.84 * z;
		float3 emission = max( 0, fade - 0.4 ) * ( glow * 0.5 + 0.01 * edge * edge * edge );
		return float4( Ns2Refract( i.vPositionSs.xy, i.vPositionSs.z, distortion, transmission, 1 ) + emission, 1 );
	}
}
