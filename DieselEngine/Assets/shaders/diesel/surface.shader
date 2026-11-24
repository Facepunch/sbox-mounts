HEADER
{
	Description = "Diesel map surface";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Transparency" );
	Feature( F_TRANSLUCENT, 0..1, "Transparency" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
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
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	#include "common/pixel.hlsl"

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Diffuse, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( Opacity, Linear, 8, "None", "_opacity", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tDiffuse < Channel( RGBA, Box( Diffuse ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tOpacity < Channel( RGBA, Box( Opacity ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flHasOpacity < Default1( 0 ); >;
	float g_flVertexColor < Default1( 0 ); >;
	float g_flAlphaReference < Default1( 0.5 ); >;
	float3 g_vTint < Default3( 1, 1, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 diffuse = Tex2DS( g_tDiffuse, g_sWrap, i.vTextureCoords.xy );
		float opacity = g_flHasOpacity > 0.5 ? Tex2DS( g_tOpacity, g_sWrap, i.vTextureCoords.xy ).r : diffuse.a;
		#if S_ALPHA_TEST
			clip( opacity - g_flAlphaReference );
		#endif

		Material m = Material::Init();
		m.Albedo = diffuse.rgb * g_vTint;
		if ( g_flVertexColor > 0.5 ) m.Albedo *= i.vColor.rgb;
		m.Normal = normalize( i.vNormalWs );
		m.Roughness = 0.8;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = S_TRANSLUCENT ? opacity : 1;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = i.vTextureCoords.xy;
		return ShadingModelStandard::Shade( i, m );
	}
}
