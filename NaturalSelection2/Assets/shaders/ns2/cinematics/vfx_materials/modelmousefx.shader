//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/modelMouseFX.surface_shader
//
//     const float distFactorAtZero = -10.0f;
//     const float distFactorAtOne  = -25.0f;
//     const float distConvergenceFactor = 100.0f;
//     const float highlightIntensityMin = 0.125f;
//
//     float3 tsNormal = DecodeNormal( tex2D(normalMap, input.texCoord) );
//     float3 vsNormal = tsNormal.x * input.vsTangent + tsNormal.y * input.vsBinormal + tsNormal.z * input.vsNormal;
//     vsNormal = normalize(vsNormal);
//
//     float3 ssDir = normalize(input.vsPosition);
//     float nrmFactor = dot(ssDir, vsNormal) + 1.0f;
//     nrmFactor = saturate(nrmFactor * nrmFactor);
//
//     float nrmConvergenceFactor = (distFactorAtOne - distFactorAtZero) * nrmFactor + distFactorAtZero;
//     float adjustedTime = max(0.0f, time - startTime);
//
//     float flashIntensity = 0.0f;
//     if (adjustedTime > 0.0f) flashIntensity = pow(2.0f, adjustedTime * nrmConvergenceFactor);
//     float3 finalFlashColor = flashColor * flashIntensity;
//
//     float highlightIntensity = nrmFactor * (1.0f - highlightIntensityMin) + highlightIntensityMin;
//     float3 finalHighlightColor = highlightColor * highlightIntensity;
//
//     material.emissive = 1.0f - ((1.0f - finalFlashColor) * (1.0f - finalHighlightColor));
//
// The commander's mouse-over highlight: a blue rim that always sits on the
// model plus a white flash that decays after startTime, decaying faster on
// surfaces facing the camera than on rim surfaces.
//
// The whole thing is built from dot(ssDir, vsNormal), and a dot product of two
// view-space vectors equals the dot product of the same two vectors in world
// space - the view transform is a rotation. So no view matrix is needed: this
// port uses the world-space normal and the world-space camera-to-fragment
// direction directly, which is both cheaper and immune to any difference
// between Spark's and s&box's view basis.
//
// ssDir points *away* from the camera, so a camera-facing normal gives
// dot == -1 and nrmFactor == 0. Rim surfaces give nrmFactor -> 1. That is why
// the rim glows: highlightIntensity runs 0.125 at the centre to 1.0 at the
// silhouette.
//
// distConvergenceFactor is declared and never used. Dropped.
//
// startTime is an absolute game timestamp. Nothing on the s&box side sets it,
// so with the default 0 `adjustedTime` is just g_flTime and the flash has long
// since decayed to nothing - leaving the steady-state rim highlight, which is
// what a static prop should show.
//
// normalMap defaults to materials/dev/dev_normal.dds via the surface_shader
// default pass in MaterialLoader.
//=============================================================================

HEADER
{
	Description = "NS2 Model Mouse FX";
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

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( NormalMap, Linear, 8, "NormalizeNormals", "_normal", ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );

	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 g_vFlashColor     < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 1.0, 1.0 ); >;
	float4 g_vHighlightColor < UiGroup( ",0/,0/0" ); Default4( 0.0, 0.5, 1.0, 1.0 ); >;
	float  g_flStartTime     < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 100000 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		const float distFactorAtZero = -10.0;
		const float distFactorAtOne = -25.0;
		const float highlightIntensityMin = 0.125;

		float2 uv = i.vTextureCoords.xy;

		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float3 normalWs = normalize( TransformNormal( Ns2DecodeNormal( normal ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs ) );

		float3 viewDir = normalize( i.vPositionWithOffsetWs.xyz );

		float nrmFactor = dot( viewDir, normalWs ) + 1.0;
		nrmFactor = saturate( nrmFactor * nrmFactor );

		float nrmConvergenceFactor = ( distFactorAtOne - distFactorAtZero ) * nrmFactor + distFactorAtZero;
		float adjustedTime = max( 0.0, g_flTime - g_flStartTime );

		float flashIntensity = adjustedTime > 0.0 ? pow( 2.0, adjustedTime * nrmConvergenceFactor ) : 0.0;
		float3 finalFlashColor = g_vFlashColor.rgb * flashIntensity;

		float highlightIntensity = nrmFactor * ( 1.0 - highlightIntensityMin ) + highlightIntensityMin;
		float3 finalHighlightColor = g_vHighlightColor.rgb * highlightIntensity;

		m.Albedo = 0.0;
		m.Emission = 1.0 - ( ( 1.0 - finalFlashColor ) * ( 1.0 - finalHighlightColor ) );

		m.Opacity = 1.0;
		m.Roughness = 1.0;
		m.Metalness = 0.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = normalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
