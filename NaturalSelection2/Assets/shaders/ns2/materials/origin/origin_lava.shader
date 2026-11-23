//=============================================================================
// Port of NS2 ns2/materials/origin/origin_lava.surface_shader
//                                                  //Made by RuneStorm, feel free to use :D
//
// This file is a deliberate duplicate of
// Assets/shaders/ns2/materials/refinery/refinery_lava.shader.
//
// origin_lava.surface_shader and refinery_lava.surface_shader are BYTE
// IDENTICAL in the shipped game - verified by hash, not by eye. RuneStorm
// authored the shader for Refinery and it was copied wholesale for Origin;
// only the .material files differ (different textures, tint and glow).
//
// It is kept as a separate file rather than shared because the mount resolves
// shaders by path: Material.cs maps <name>.surface_shader to
// shaders/ns2/<name>.vmat, so a material referencing materials/origin/origin_lava
// needs a file at exactly that path or it silently falls back to model.vmat.
//
// KEEP THE TWO IN SYNC. Any fix to one belongs in the other.
//
//     lava1 = LavaTexture ( uv * ( .5,  .5 ) + (time *  .06,  time *  0.04) * ScrollSpeedMultiplier )
//     lava2 = LavaTexture ( uv * (-.35, .35) + (time * -.01,  time * -0.03) * ScrollSpeedMultiplier )
//     lava3 = LavaTexture ( uv * ( 1,  -1  ) + (time *  .001, time * -0.06) * ScrollSpeedMultiplier )
//
//     noise = NoiseTexture( uv * (-.15, .15) + (time *  .003, time * -0.05) * S ).r
//           + NoiseTexture( uv * ( .25, .25) + (time * -.002, time *  0.04) * S ).r
//
//     blend = (lava1 * .2) + (clamp((lava2 - .95 * 2), 0, 1) + (lava3 * .2)) + (noise * .2);
//
//     material.wsOffset = input.wsNormal * (sin(time * 0.4) * WaveIntensity);
//     material.albedo   = blend * SurfaceTint + SurfaceTintDark;
//     material.emissive = (blend * SurfaceTint) * GlowIntensity;
//     material.id       = showDecals;
//
// Writes ALBEDO, so this is a real lit, depth-writing deferred surface - it
// occludes, takes decals and receives shadows - with the emissive term riding
// on top. Built on the level_emissive template for that reason.
//
// PORTED AS AUTHORED, INCLUDING A BUG: clamp((lava2 - .95 * 2), 0, 1) is
// clamp(lava2 - 1.9, 0, 1) because * binds tighter than -. lava2 comes from a
// 0..1 texture, so that term is ALWAYS ZERO and the second lava layer
// contributes nothing. The author almost certainly meant
// clamp((lava2 - .95) * 2, 0, 1). "Fixing" it would make the lava visibly
// brighter and more contrasty than it is in NS2, so it is left as shipped.
//
// wsOffset is a global sin(time * 0.4) with no spatial term, so the surface
// swells uniformly rather than rippling. WaveIntensity is authored in metres,
// converted to inches.
//
// material.id carries the decal-receive flag (0.5 = no decals). s&box has no
// equivalent channel, so it is ignored here as elsewhere in the port.
//=============================================================================

HEADER
{
	Description = "NS2 Origin Lava";
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
	#define S_ALPHA_TEST 0
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

	float g_flWaveIntensity < UiGroup( ",0/,0/0" ); Default1( 0.05 ); Range1( 0, 16 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput o = ProcessVertex( v );

		// Spark: material.wsOffset = wsNormal * (sin(time * 0.4) * WaveIntensity),
		// authored in metres.
		o.vPositionWs.xyz += o.vNormalWs.xyz * ( sin( g_flTime * 0.4 ) * g_flWaveIntensity * 40.0 );
		o.vPositionPs = Position3WsToPs( o.vPositionWs.xyz );

		return FinalizeVertex( o );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	// twoSided in the original.
	RenderState( CullMode, NONE );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( LavaTexture,  Linear, 8, "None", "_color", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NoiseTexture, Linear, 8, "None", "_noise", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tLavaTexture  < Channel( RGBA, Box( LavaTexture ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNoiseTexture < Channel( RGBA, Box( NoiseTexture ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float3 g_vSurfaceTint     < UiGroup( ",0/,0/0" ); Default3( 0.1, 0.02, 0.0 ); >;
	float3 g_vSurfaceTintDark < UiGroup( ",0/,0/0" ); Default3( 0.0, 0.0, 0.0 ); >;
	float g_flGlowIntensity         < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 128 ); >;
	float g_flScrollSpeedMultiplier < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;
	float g_flDecals                < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;
		float s = g_flScrollSpeedMultiplier;

		float3 lava1 = Ns2SrgbToLinear( Tex2DS( g_tLavaTexture, g_sWrap, uv * float2(  0.5,  0.5  ) + float2( t *  0.06,  t *  0.04 ) * s ).rgb );
		float3 lava2 = Ns2SrgbToLinear( Tex2DS( g_tLavaTexture, g_sWrap, uv * float2( -0.35, 0.35 ) + float2( t * -0.01,  t * -0.03 ) * s ).rgb );
		float3 lava3 = Ns2SrgbToLinear( Tex2DS( g_tLavaTexture, g_sWrap, uv * float2(  1.0, -1.0  ) + float2( t *  0.001, t * -0.06 ) * s ).rgb );

		float noise = Ns2SrgbToLinear( Tex2DS( g_tNoiseTexture, g_sWrap, uv * float2( -0.15, 0.15 ) + float2( t *  0.003, t * -0.05 ) * s ).rgb ).r
		            + Ns2SrgbToLinear( Tex2DS( g_tNoiseTexture, g_sWrap, uv * float2(  0.25, 0.25 ) + float2( t * -0.002, t *  0.04 ) * s ).rgb ).r;

		// See header: (lava2 - .95 * 2) is (lava2 - 1.9) in the original, so this
		// clamp is always 0. Reproduced verbatim.
		float3 blend = ( lava1 * 0.2 ) + ( clamp( ( lava2 - 0.95 * 2.0 ), 0.0, 1.0 ) + ( lava3 * 0.2 ) ) + ( noise * 0.2 );

		float3 albedo = blend * g_vSurfaceTint + g_vSurfaceTintDark;
		float3 emissive = ( blend * g_vSurfaceTint ) * g_flGlowIntensity;

		Material m = Material::Init();

		// The original writes no specular or gloss, so Spark leaves both at zero.
		Ns2ApplySpecularGloss( m, albedo, float3( 0.0, 0.0, 0.0 ), 0.0 );

		m.Emission = emissive;
		m.Opacity = 1.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = i.vNormalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
