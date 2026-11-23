//=============================================================================
// Port of NS2 ns2/materials/origin/conveyor.surface_shader
//
//     float2 movement = input.texCoord + float2(time * Scroll, 0);
//     float4 specularGloss = tex2D(specularMap, movement);
//     material.albedo   = tex2D(albedoMap, movement);
//     material.tsNormal = DecodeNormal( tex2D(normalMap, movement) );
//     material.specular = specularGloss.rgb;
//
// A conveyor belt: Model with the whole texture set scrolled on u.
//
// Two NS2 quirks kept as-is:
//
//  * material.gloss is never assigned, so the belt takes whatever Spark's
//    default gloss is (0). Written as zero here rather than pulling
//    specularGloss.a, which would change the look.
//  * normalMap and specularMap are flagged srgb="true", which is almost
//    certainly an authoring mistake - a normal map is not colour data. Spark
//    hardware-decodes them anyway, so we decode them too: the normal is
//    decoded before Ns2DecodeNormal and the specular goes through
//    Ns2ApplySpecularGloss, which decodes it as usual. Removing the normal
//    decode would visibly flatten the belt relative to the original.
//
// albedoMap is assigned as a float4 to a float3 in NS2; the alpha is dropped.
//=============================================================================

HEADER
{
	Description = "NS2 Conveyor";
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
	float4 vTintColor : COLOR1;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;

		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;

		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap,   Linear, 8, "None",             "_color",  ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal", ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap, Linear, 8, "None",             "_spec",   ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flScroll < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( -4, 4 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy + float2( g_flTime * g_flScroll, 0.0 );

		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb;
		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );

		normal.rgb = Ns2SrgbToLinear( normal.rgb );

		Ns2ApplySpecularGloss( m, albedo * i.vTintColor.rgb, specularGloss.rgb, 0.0 );

		m.Opacity = 1.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = TransformNormal( Ns2DecodeNormal( normal ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
