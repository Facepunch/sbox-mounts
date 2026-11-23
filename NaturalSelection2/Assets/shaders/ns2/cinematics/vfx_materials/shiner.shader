//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/shiner.surface_shader
//
//     const float shimmerDuration = 0.333f;
//     const float invShimmerDuration = 1.0f / shimmerDuration;
//     const float totalDuration = 3.0f;
//     const float cycleFactor = 0.4f / totalDuration;
//     const float tau = 6.28318530718f;
//     const float fadeSpeed = 0.3f;
//
//     float cycleTime = time - startTime;
//     cycleTime *= cycleFactor;
//     cycleTime = cycleTime - floor(cycleTime);
//     cycleTime *= totalDuration;
//
//     float theta = saturate(cycleTime * invShimmerDuration) * tau;
//     float3 nrm = input.vsNormal;
//     float3 shimmerVec = float3(0.0f, sin(theta), cos(theta));
//
//     float fadeFactor = saturate((-2.0f * fadeSpeed * invShimmerDuration * abs(cycleTime - 0.5f * shimmerDuration)) + fadeSpeed);
//     float shineValue = saturate(dot(shimmerVec, nrm)) * fadeFactor;
//
//     material.emissive = glowColor * (shineValue * glowAlpha);
//
// A periodic specular "shine" that sweeps across a model: shimmerVec rotates a
// full turn in the view-space YZ plane over 0.333s, then the surface sits dark
// until the 7.5s cycle repeats.
//
// Note the cycle period is NOT totalDuration. cycleFactor is 0.4/3, so
// `cycleTime - floor(cycleTime)` wraps every 1/cycleFactor = 7.5 seconds, and
// the rescale by totalDuration makes cycleTime run 0..3 over that window. Only
// the first 0.333s of each 7.5s window shows anything.
//
// This is the only NS2 shader ported so far that genuinely needs the view
// basis rather than just a dot product, because shimmerVec is expressed in
// view-space axes. s&box exposes g_matWorldToView (core/shaders/common.fxc),
// so the world normal is transformed into view space with Vector3WsToVs.
//
// s&box and Spark may disagree on the sign of the view-space up axis, which
// would make the shimmer sweep top-to-bottom instead of bottom-to-top. The
// effect is symmetric over a full rotation, so that is not visually
// distinguishable in practice.
//
// startTime is a game-driven absolute timestamp used to phase-align the shine
// across a group of models. Nothing on the s&box side sets it; the default 0
// simply leaves every material in phase with engine time.
//=============================================================================

HEADER
{
	Description = "NS2 Shiner";
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
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	float4 g_vGlowColor < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 1.0, 1.0 ); >;
	float  g_flGlowAlpha < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;
	float  g_flStartTime < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 100000 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		const float shimmerDuration = 0.333;
		const float invShimmerDuration = 1.0 / shimmerDuration;
		const float totalDuration = 3.0;
		const float cycleFactor = 0.4 / totalDuration;
		const float tau = 6.28318530718;
		const float fadeSpeed = 0.3;

		float cycleTime = ( g_flTime - g_flStartTime ) * cycleFactor;
		cycleTime = cycleTime - floor( cycleTime );
		cycleTime *= totalDuration;

		float theta = saturate( cycleTime * invShimmerDuration ) * tau;

		float3 normalWs = normalize( i.vNormalWs );
		float3 nrm = Vector3WsToVs( normalWs );

		float3 shimmerVec = float3( 0.0, sin( theta ), cos( theta ) );

		float fadeFactor = saturate( ( -2.0 * fadeSpeed * invShimmerDuration * abs( cycleTime - 0.5 * shimmerDuration ) ) + fadeSpeed );
		float shineValue = saturate( dot( shimmerVec, nrm ) ) * fadeFactor;

		m.Albedo = 0.0;
		m.Emission = g_vGlowColor.rgb * ( shineValue * g_flGlowAlpha );

		m.Opacity = 1.0;
		m.Roughness = 1.0;
		m.Metalness = 0.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = normalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = i.vTextureCoords.xy;

		return ShadingModelStandard::Shade( i, m );
	}
}
