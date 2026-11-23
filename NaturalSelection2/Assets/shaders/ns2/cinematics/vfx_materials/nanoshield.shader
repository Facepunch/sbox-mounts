//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/nanoshield.surface_shader
//
//     const float3 tint = float3(.8, 6, 6);
//     material.wsOffset = input.wsNormal * 0.08;
//
//     float3 greeb    = tex2D( greebMap, input.texCoord * 6 ).rgb;
//     float3 circuit2 = tex2D( circuitMap, input.texCoord * float2(10, 9) ).rgb;
//     float  fresnel  = pow(max(-input.vsNormal.z, 0), 10);
//     float3 noise    = tex2D( noiseMap, (input.texCoord + float2(0, time * 1)) * float2(1, 0.5) ).rgb;
//
//     material.emissive = ((greeb * noise + circuit2) * fresnel) * tint;
//
// The cyan nanoshield that wraps a marine structure. Three things carry it:
//
//  - wsOffset pushes the shell 0.08 METRES along the normal so it floats clear
//    of the model it is skinning instead of z-fighting with it. Spark authors
//    world distances in metres and s&box works in inches, so this must be
//    scaled by NS2_METRE - without that it becomes 0.08 inches, 39x too small
//    to clear anything.
//  - fresnel is pow(N dot V, 10), an unusually tight exponent. Note the sense:
//    most NS2 vfx use pow(1 - NdotV, n) to rim-light the silhouette, but this
//    one takes NdotV directly, so the shield is brightest where it FACES the
//    camera and fades out at grazing angles. That is what makes it read as a
//    solid plated shell rather than a bubble outline.
//  - the greeb detail is modulated by vertically scrolling noise while the
//    circuit layer is added raw, so the circuitry stays constant while energy
//    washes over the panelling.
//
// Only emissive is written, so this is the WriteEmissive technique alone: no
// G-buffer, no depth write, purely additive.
//=============================================================================

HEADER
{
	Description = "NS2 Nanoshield";
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
		PixelInput o = ProcessVertex( v );

		// Spark: material.wsOffset = input.wsNormal * 0.08 (metres -> inches).
		o.vPositionWs.xyz += o.vNormalWs.xyz * ( 0.08 * 40.0 );
		o.vPositionPs = Position3WsToPs( o.vPositionWs.xyz );

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
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( GreebMap,   Srgb,   8, "None", "_greeb",   ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( CircuitMap, Srgb,   8, "None", "_circuit", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NoiseMap,   Linear, 8, "None", "_noise",   ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tGreebMap   < Channel( RGBA, Box( GreebMap ),   Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tCircuitMap < Channel( RGBA, Box( CircuitMap ), Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tNoiseMap   < Channel( RGBA, Box( NoiseMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		const float3 tint = float3( 0.8, 6.0, 6.0 );

		float2 uv = i.vTextureCoords.xy;

		float3 greeb    = Tex2DS( g_tGreebMap, g_sWrap, uv * 6.0 ).rgb;
		float3 circuit2 = Tex2DS( g_tCircuitMap, g_sWrap, uv * float2( 10.0, 9.0 ) ).rgb;
		float3 noise    = Tex2DS( g_tNoiseMap, g_sWrap, ( uv + float2( 0.0, g_flTime ) ) * float2( 1.0, 0.5 ) ).rgb;

		// max(-vsNormal.z, 0) in Spark's view space is N dot V. Note this is not
		// inverted - the shield is brightest facing the camera.
		float nDotV = saturate( dot( normalize( i.vNormalWs ), normalize( -i.vPositionWithOffsetWs.xyz ) ) );
		float fresnel = pow( nDotV, 10.0 );

		float3 emissive = ( ( greeb * noise + circuit2 ) * fresnel ) * tint;

		return float4( emissive, 1.0 );
	}
}
