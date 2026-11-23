//=============================================================================
// Port of NS2 ns2/models/props/origin/biodome_foliage_tree_leaves.surface_shader
//
//     float4 specularGloss = tex2D(specularMap, input.texCoord);
//
//     material.albedo   = tex2D(albedoMap, input.texCoord).rgb;
//     material.tsNormal = DecodeNormal( tex2D(normalMap, input.texCoord) );
//     material.specular = specularGloss.rgb;
//     material.gloss    = specularGloss.a;
//     material.opacity  = tex2D(opacityMap, input.texCoord).r;
//     material.id       = decals;
//
// <param name="alphaTest">true</param> and <param name="twoSided">true</param>.
//
// The plain opaque model shader with alpha testing and no backface culling -
// standard foliage. Because alphaTest is set rather than translucent, opacity
// is a hard cutout, not a blend, so the usual translucency caveats do not
// apply here and the surface lights and shadows normally.
//
// material.id carries the decal receive flag, which has no analogue here.
//=============================================================================

HEADER
{
	Description = "NS2 Biodome Foliage Tree Leaves";
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
	#define S_ALPHA_TEST 1
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
	#include "ns2/alpha_test.hlsl"
	#include "ns2/ns2.hlsl"

	RenderState( CullMode, NONE );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap,   Srgb,   8, "None",             "_color",   ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",  ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap, Linear, 8, "None",             "_spec",    ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( OpacityMap,  Linear, 8, "None",             "_opacity", ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tOpacityMap  < Channel( RGBA, Box( OpacityMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		m.Opacity = Tex2DS( g_tOpacityMap, g_sWrap, uv ).r;

		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb;
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );

		Ns2ApplySpecularGloss( m, albedo, specularGloss.rgb, specularGloss.a );

		m.Normal = TransformNormal( Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv ) ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return Ns2ShadeAlphaTest( i, m, TextureDimensions2DS( g_tOpacityMap, 0 ), g_flAlphaTestReference );
	}
}
