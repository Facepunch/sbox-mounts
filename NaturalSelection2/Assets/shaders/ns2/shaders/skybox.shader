//=============================================================================
// Port of NS2 core/shaders/SkyBox.surface_shader
//
//     <input name="emissiveMap" type="texture" address="clamp" srgb="true"/>
//
//     float4 tex = tex2D(emissiveMap, input.texCoord);
//     material.albedo   = float3(0, 0, 0);
//     material.emissive = tex.rgb * input.color.rgb;
//     material.opacity  = tex.a * input.color.a;
//
// Unlike Emissive.surface_shader this writes albedo as well, so Spark runs it
// through the opaque WriteDeferred technique rather than the additive
// WriteEmissive one - a black surface that only contributes emissive light.
// Opacity lands in the unused G-buffer alpha, so it is pinned to 1 here.
//
// The emissive map is clamp addressed to stop the sky seams bleeding.
//=============================================================================

HEADER
{
	Description = "NS2 SkyBox";
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

	SamplerState g_sClamp < Filter( ANISO ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_selfillum", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float4 tex = Tex2DS( g_tEmissiveMap, g_sClamp, i.vTextureCoords.xy );

		m.Albedo = float3( 0.0, 0.0, 0.0 );
		m.Metalness = 0.0;
		m.Roughness = 1.0;
		m.Emission = Ns2SrgbToLinear( tex.rgb ) * i.vColor.rgb;
		m.Opacity = 1.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = i.vNormalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = i.vTextureCoords.xy;

		return ShadingModelStandard::Shade( i, m );
	}
}
