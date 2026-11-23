#ifndef NS2_REFRACT_HLSL
#define NS2_REFRACT_HLSL

#include "common/classes/Depth.hlsl"

//=============================================================================
// Spark's screen-space refraction, ported from core/renderer/Deferred.hlsl,
// Refraction.hlsl and Deferred.render_setup.
//
// A surface shader never samples the scene itself. It writes two outputs:
//
//     material.ssDistortion       // float2, a NORMALISED screen UV offset
//     material.transmissionColor  // float3, tints whatever ends up behind
//
// and the deferred renderer resolves them in three separate places. It is
// worth stating the real pipeline, because the obvious reading - "the object
// samples the background and draws it" - is wrong in a way that changes how
// these shaders have to be ported.
//
// 1. WriteRefractionMap draws the refractor into a screen sized RGBA16F
//    "refraction" buffer, additively, and writes NO colour and NO depth:
//
//        return float4( material.ssDistortion * material.opacity, 0, input.depth.r );
//
//    So the refractor deposits an offset and its own depth, and that is all.
//    Because the blend is one/one, overlapping refractors accumulate.
//
// 2. A fullscreen pass (Refraction.hlsl, RefractionDistortionHighQ) then warps
//    the ALREADY LIT scene by that offset:
//
//        float2 distorted = texCoord + refractionMap.xy;
//        if ( distortedDepth - refractorDepth >= bias ) use lightTexture(distorted);
//        else                                           use lightTexture(texCoord);
//
//    The depth test is the important part: a sample is only accepted if what
//    it pulls in is BEHIND the refractor. Without it, anything standing in
//    front of the glass smears across it. Spark also biases the comparison so
//    that the view model never distorts, fading the bias out with distance.
//
// 3. transmissionColor is a completely separate pass, WriteTransmission, with
//    blend zero/src_color, i.e. a multiply over the destination:
//
//        return float4( lerp( 1, material.transmissionColor, material.opacity ), 0 );
//
//    Note the lerp: opacity controls how far the tint is applied, so an
//    opacity of 0 leaves the scene untouched no matter what the tint is.
//
// The net effect on a refractor's own pixels is therefore
//
//     result = warp( scene, ssDistortion * opacity ) * lerp( 1, transmissionColor, opacity )
//
// which is what Ns2Refract reproduces below. Doing it per-object against the
// framebuffer copy is not how Spark schedules the work, but over the pixels a
// refractor actually covers it is the same image, and it is the only option in
// a forward pass. The one behaviour that must be kept is the depth rejection.
//
// A refractor writes no depth and no colour of its own, so a port must not
// enable depth writes: it replaces its pixels with a warped copy of the scene
// and nothing else.
//=============================================================================

Texture2D g_tNs2FrameBufferCopy < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;

float3 Ns2SampleScene( float2 vPositionSs )
{
	float2 uv = CalculateViewportUv( vPositionSs ) * g_vFrameBufferCopyInvSizeAndUvScale.zw;
	return g_tNs2FrameBufferCopy.SampleLevel( g_sTrilinearMirror, uv, 0 ).rgb;
}

// Spark tunes its depth bias in metres. s&box works in inches, so the two
// distances at which the bias is full and zero are converted here rather than
// being silently retuned. NS2_METRE lives in ns2.hlsl.
#ifndef NS2_METRE
#define NS2_METRE 40.0
#endif

// Mirrors PerformRefraction's single sample path, including the depth test that
// stops foreground geometry bleeding onto the refractor.
float3 Ns2WarpScene( float2 vPositionSs, float flDeviceDepth, float2 ssDistortion )
{
	float2 vDistortedSs = vPositionSs + ssDistortion / g_vInvViewportSize;

	float flRefractorDepth = Depth::Linearize( flDeviceDepth, vPositionSs );
	float flDistortedDepth = Depth::GetLinear( vDistortedSs );

	const float flBiasDepth1 = 0.75 * NS2_METRE;
	const float flBiasDepth2 = 1.25 * NS2_METRE;
	const float flMaxBias = 0.75 * NS2_METRE;

	float flBias = saturate( ( flDistortedDepth - flBiasDepth2 ) / ( flBiasDepth1 - flBiasDepth2 ) ) * flMaxBias;

	bool bValid = ( flDistortedDepth - flRefractorDepth ) >= flBias;

	return Ns2SampleScene( bValid ? vDistortedSs : vPositionSs );
}

float3 Ns2Refract( float2 vPositionSs, float flDeviceDepth, float2 ssDistortion, float3 transmissionColor, float opacity )
{
	float3 scene = Ns2WarpScene( vPositionSs, flDeviceDepth, ssDistortion * opacity );

	return scene * lerp( float3( 1.0, 1.0, 1.0 ), transmissionColor, opacity );
}

// Spark builds its distortion from a tangent space normal scaled by the view
// space tangent/binormal (input.vsTangent / input.vsBinormal), which projects
// the perturbation into screen space. The tangent basis is available per-pixel
// here, so rotate the perturbation into world space and then into view space,
// whose x/y axes are screen right and screen up - screen V runs downwards,
// hence the negated y.
//
// ns2/cinematics/vfx_materials/distort and models/alien/gorge/web_distort both
// write "vsNormal.xy * float2(1, -1)" by hand, which is the same correction and
// confirms the sign. Shaders that omit it (water_refract and the vtxcolor
// distort family) are inconsistent in the original, and are ported as authored.
//
// The magnitude stays in the material's own constant - scale1, normIntensity
// and friends are all around 0.01 - exactly as Spark tuned them.
float2 Ns2DistortionToScreen( float3 tsNormal, float3 vTangentUWs, float3 vTangentVWs )
{
	float3 wsPerturb = tsNormal.x * vTangentUWs + tsNormal.y * vTangentVWs;
	float3 vsPerturb = Vector3WsToVs( wsPerturb );

	return float2( vsPerturb.x, -vsPerturb.y );
}

#endif // NS2_REFRACT_HLSL
