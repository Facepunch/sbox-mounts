//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/rs_forcefield.surface_shader
//
//     float distanceblend(const float range, const float offset, float3 vsPos)
//     {
//         return saturate((sqrt(vsPos.x*vsPos.x + vsPos.y*vsPos.y + vsPos.z*vsPos.z) - (offset / 3.5)) / range);
//     }
//
//     const float scanlineScale = 1.0 / 64;
//     const float3 laserCenterGlow = float3(10,10,10);
//
//     float laserSetup    = saturate((abs(frac(input.wsPosition.y / laserDistance) - 0.5) * 2) - 0.9);
//     float laserGradient = saturate(lerp(0.1, 1, pow(1 - (abs(frac(input.texCoord.r) - 0.5) * 2), 3)));
//     float3 laserGrid    = saturate(laserSetup * laserGradient) * lerp(laserColor, laserCenterGlow, laserSetup * 10) * 10;
//
//     float2 pan1    = input.texCoord + (time * float2(0.2, 0.056));
//     float2 pan2    = input.texCoord + (time * float2(-0.105, -0.124));
//     float2 pjitter = lerp(pan2, pan1, floor(tex2D(noisetex, pan1).rg + noiseMidpoint));
//
//     float2 distortionUV = tex2D(noisetex, pjitter).rg * 0.1 - (input.texCoord * noiseScale);
//     float scanlines = saturate(abs(frac(input.texCoord.y / scanlineScale + (1-time)) - 0.5) + scanlineIntensity);
//
//     float noise1 = tex2D(noisetex, distortionUV + (time * float2(-0.1, 0.1))).r;
//     float noise2 = saturate(tex2D(noisetex, distortionUV * 1.5 + (time * float2(0.05, -0.0754))).r - noise1) + (noise1 * 0.2);
//     float3 noiseVis = (noise2 * noiseColor) + laserGrid;
//     float3 forcefieldBlend = lerp(noiseVis, laserGrid, distanceblend(lerp(1,4,noise2), lerp(3,4,noise2), input.vsPosition));
//
//     material.emissive     = forcefieldBlend * glowCorrection;
//     material.ssDistortion = float3(noise2, noise2 * 0.1, 1) * float3(distortionIntensity, distortionIntensity, 1);
//
// RuneStorm's forcefield. The most involved of the family: it is the only one
// that writes BOTH emissive AND ssDistortion, so it needs the full refraction
// path (framebuffer copy + depth-validated warp) on top of the additive glow.
// The composite is therefore warp(scene) + emissive, written with a ONE/ZERO
// blend rather than an additive one, exactly as in distort.shader.
//
// The interesting part is the laser grid. laserSetup slices world space into
// horizontal bands: frac(height / laserDistance) is a 0..1 sawtooth per band,
// abs(x - 0.5) * 2 folds it into a V, and subtracting 0.9 keeps only the top
// 10% - a thin bright line every laserDistance metres, locked to WORLD space,
// so the lines stay put as the surface or camera moves. laserGradient does the
// same trick across U to fade the lines out at the panel edges.
//
// lerp(laserColor, laserCenterGlow, laserSetup * 10) then blows the very centre
// of each line out to white (10,10,10) while the shoulders stay laserColor
// (40,1,0) - a red line with a white-hot core.
//
// CONVERSIONS:
//   - input.wsPosition.y is VERTICAL in Spark (Y-up). In s&box that is .z.
//     Getting this wrong rotates the laser bands 90 degrees.
//   - wsPosition is absolute world space, but s&box gives camera-relative
//     positions, so g_vCameraPositionWs is added back. The bands must be
//     anchored to the world or they would swim with the camera.
//   - laserDistance (0.2) and distanceblend's range/offset are METRES.
//
// DELIBERATE FIDELITY NOTES:
//   - scanlines is computed and never used in the original. Omitted, along
//     with scanlineIntensity/scanlineScale, rather than invented into the
//     output. Same for the unused DecodeNormal helper.
//   - Spark's local frac() is float f = value - floor(value), which is exactly
//     HLSL's frac(), so the intrinsic is used directly.
//   - material.ssDistortion is a float2 and the original assigns a float3 to
//     it, so the third component (1) is TRUNCATED AWAY. The distortion is
//     (noise2 * distortionIntensity, noise2 * 0.1 * distortionIntensity) -
//     ten times stronger horizontally than vertically.
//   - noisetex has no srgb attribute, unlike most NS2 inputs, so it is read
//     as linear data with no sRGB decode.
//
// twoSided in the original - a forcefield is visible from both sides.
//=============================================================================

HEADER
{
	Description = "NS2 RS Forcefield";
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

	// twoSided in the original.
	RenderState( CullMode, NONE );

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ZERO );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	// No srgb attribute on this input in the original - read as linear data.
	CreateInputTexture2D( Noisetex, Linear, 8, "None", "_noise", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	Texture2D g_tNoisetex < Channel( RGBA, Box( Noisetex ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float3 g_vLaserColor     < UiGroup( ",0/,0/0" ); Default3( 40.0, 1.0, 0.0 ); >;
	float3 g_vGlowCorrection < UiGroup( ",0/,0/0" ); Default3( 0.7126, 0.1152, 0.0722 ); >;
	float3 g_vNoiseColor     < UiGroup( ",0/,0/0" ); Default3( 1.0, 0.02, 0.0 ); >;
	float g_flNoiseScale          < UiGroup( ",0/,0/0" ); Default1( 0.4 );   Range1( 0, 8 ); >;
	float g_flDistortionIntensity < UiGroup( ",0/,0/0" ); Default1( 0.015 ); Range1( 0, 1 ); >;
	float g_flNoiseMidpoint       < UiGroup( ",0/,0/0" ); Default1( 0.75 );  Range1( 0, 1 ); >;
	float g_flLaserDistance       < UiGroup( ",0/,0/0" ); Default1( 0.2 );   Range1( 0.001, 8 ); >;

	// Spark: saturate((sqrt(dot(vsPos,vsPos)) - (offset / 3.5)) / range).
	// range and offset are authored in metres, so both are converted here.
	float Ns2DistanceBlend( float flRange, float flOffset, float flDistance )
	{
		return saturate( ( flDistance - ( ( flOffset * NS2_METRE ) / 3.5 ) ) / ( flRange * NS2_METRE ) );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		const float3 laserCenterGlow = float3( 10.0, 10.0, 10.0 );

		// Spark's input.wsPosition.y is the VERTICAL axis (Y-up) and is absolute
		// world space, so the camera position is added back to s&box's
		// camera-relative position and .z is taken instead of .y.
		float flHeightWs = i.vPositionWithOffsetWs.z + g_vCameraPositionWs.z;
		float flLaserDistance = g_flLaserDistance * NS2_METRE;

		float laserSetup = saturate( ( abs( frac( flHeightWs / flLaserDistance ) - 0.5 ) * 2.0 ) - 0.9 );
		float laserGradient = saturate( lerp( 0.1, 1.0, pow( 1.0 - ( abs( frac( uv.r ) - 0.5 ) * 2.0 ), 3.0 ) ) );
		float3 laserGrid = saturate( laserSetup * laserGradient ) * lerp( g_vLaserColor, laserCenterGlow, laserSetup * 10.0 ) * 10.0;

		float2 pan1 = uv + ( t * float2(  0.2,   0.056  ) );
		float2 pan2 = uv + ( t * float2( -0.105, -0.124 ) );
		float2 pjitter = lerp( pan2, pan1, floor( Tex2DS( g_tNoisetex, g_sWrap, pan1 ).rg + g_flNoiseMidpoint ) );

		float2 distortionUV = Tex2DS( g_tNoisetex, g_sWrap, pjitter ).rg * 0.1 - ( uv * g_flNoiseScale );

		float noise1 = Tex2DS( g_tNoisetex, g_sWrap, distortionUV + ( t * float2( -0.1, 0.1 ) ) ).r;
		float noise2 = saturate( Tex2DS( g_tNoisetex, g_sWrap, distortionUV * 1.5 + ( t * float2( 0.05, -0.0754 ) ) ).r - noise1 ) + ( noise1 * 0.2 );

		float3 noiseVis = ( noise2 * g_vNoiseColor ) + laserGrid;

		// distanceblend takes input.vsPosition and uses its full length, so this
		// is a radial distance from the camera, not a planar view depth.
		float flViewDistance = length( i.vPositionWithOffsetWs.xyz );
		float blend = Ns2DistanceBlend( lerp( 1.0, 4.0, noise2 ), lerp( 3.0, 4.0, noise2 ), flViewDistance );

		float3 forcefieldBlend = lerp( noiseVis, laserGrid, blend );
		float3 emissive = forcefieldBlend * g_vGlowCorrection;

		// float3 assigned to a float2 in the original - the trailing 1 is dropped.
		float2 ssDistortion = float2( noise2, noise2 * 0.1 ) * g_flDistortionIntensity;

		// No transmissionColor or opacity written, so both keep Spark's defaults
		// of 1: the transmission pass is identity and the warp is applied in full.
		float3 scene = Ns2Refract( i.vPositionSs.xy, i.vPositionSs.z, ssDistortion, float3( 1.0, 1.0, 1.0 ), 1.0 );

		return float4( scene + emissive, 1.0 );
	}
}
