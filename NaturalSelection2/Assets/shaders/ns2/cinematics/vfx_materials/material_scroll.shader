//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/material_scroll.surface_shader
//
//     <param name="twoSided">true</param>
//
//     float2 offset = float2( time * speed, time * speedY );
//     float4 specularGloss = tex2D(specularMap, input.texCoord + offset);
//     material.albedo   = tex2D(albedoMap, input.texCoord + offset).rgb;
//     material.tsNormal = DecodeNormal( tex2D(normalMap, input.texCoord + offset) );
//     material.specular = specularGloss.rgb;
//     material.gloss    = specularGloss.a;
//     material.id       = decals;
//
// A plain opaque Model surface with all three maps scrolled together, used for
// conveyor belts, steam sheets and similar. Nothing else differs from
// shaders/model.
//
// The NS2 source assigns `float showDecals = decals;` and then uses it once.
// Collapsed here.
//
// material.id drives Spark's decal and wetness passes, which s&box has no
// equivalent for. The input is kept so materials still bind, but it is inert.
//
// twoSided => no culling. See model_alpha_double.shader for the full rationale.
//=============================================================================

HEADER
{
	Description = "NS2 Material Scroll";
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

	RenderState( CullMode, NONE );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap,   Linear, 8, "None",             "_color",  ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal", ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap, Linear, 8, "None",             "_spec",   ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flDecals < UiGroup( ",0/,0/0" ); Default1( 0.0 );  Range1( 0, 1 ); >;
	float g_flSpeed  < UiGroup( ",0/,0/0" ); Default1( 0.03 ); Range1( -4, 4 ); >;
	float g_flSpeedY < UiGroup( ",0/,0/0" ); Default1( 0.01 ); Range1( -4, 4 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 offset = float2( g_flTime * g_flSpeed, g_flTime * g_flSpeedY );
		float2 uv = i.vTextureCoords.xy + offset;

		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb;
		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );

		Ns2ApplySpecularGloss( m, albedo * i.vTintColor.rgb, specularGloss.rgb, specularGloss.a );

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
