//=============================================================================
// Port of NS2 ns2/shaders/skybox_dark_twinkle.surface_shader
//
//     float4 tex   = tex2D(emissiveMap, input.texCoord);
//     float4 noise = tex2D(twinkleMap, input.texCoord + float2(time * 0.2f, time * 0.4f) * twinkleSpeed);
//     float4 twinkle = (tex.a * noise.a) * 8.0f;
//     material.albedo   = float3(0, 0, 0);
//     material.emissive = (pow(tex.rgb, 1.5)) * twinkle.a + (pow(tex.rgb, 1.5));
//
// Star field: the emissive map's alpha channel masks which texels are allowed
// to sparkle, the scrolling noise map's alpha drives when. Base stars are
// always drawn at pow(rgb, 1.5) and the twinkle adds up to 8x on top.
//
// twinkle is declared float4 from a scalar, so every channel holds the same
// value and .a is just that scalar. Collapsed to a float here.
//
// emissiveMap is srgb="true", twinkleMap is not, so tex.rgb is decoded before
// the pow while tex.a and noise.a stay raw - exactly what Spark's samplers do.
//
// albedo is written as black, so this still takes Spark's WriteDeferred pass
// and occludes whatever is behind it, unlike a pure WriteEmissive surface.
//=============================================================================

HEADER
{
	Description = "NS2 Skybox Dark Twinkle";
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

	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissive", ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( TwinkleMap,  Linear, 8, "None", "_mask",     ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tTwinkleMap  < Channel( RGBA, Box( TwinkleMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flTwinkleSpeed < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		float4 tex = Tex2DS( g_tEmissiveMap, g_sWrap, uv );
		float4 noise = Tex2DS( g_tTwinkleMap, g_sWrap, uv + float2( g_flTime * 0.2, g_flTime * 0.4 ) * g_flTwinkleSpeed );

		float twinkle = ( tex.a * noise.a ) * 8.0;

		float3 stars = pow( Ns2SrgbToLinear( tex.rgb ), 1.5 );

		m.Albedo = 0.0;
		m.Emission = stars * twinkle + stars;

		m.Opacity = 1.0;
		m.Roughness = 1.0;
		m.Metalness = 0.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = i.vNormalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
