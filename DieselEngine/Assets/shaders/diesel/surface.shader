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
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
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
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#if S_RENDER_BACKFACES
		RenderState( CullMode, NONE );
	#else
		RenderState( CullMode, BACK );
	#endif

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Diffuse, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( Opacity, Linear, 8, "None", "_opacity", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tDiffuse < Channel( RGBA, Box( Diffuse ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tOpacity < Channel( RGBA, Box( Opacity ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NormalMap, Linear, 8, "None", "_normal", ",0/,0/0", Default4( 0.5, 0.5, 1, 0.5 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flHasNormal < Default1( 0 ); >;
	float g_flNormalRA < Default1( 0 ); >;
	float g_flHasOpacity < Default1( 0 ); >;
	float g_flVertexColor < Default1( 0 ); >;
	float g_flBackdrop < Default1( 0 ); >;
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
		// Diesel backdrop surfaces carry their own lighting. Shading the enormous sky
		// dome as an ordinary surface introduces self-shadow bands and triangle seams.
		if ( g_flBackdrop > 0.5 )
			return float4( m.Albedo, S_TRANSLUCENT ? opacity : 1 );
		m.Normal = normalize( i.vNormalWs );
		if ( g_flHasNormal > 0.5 )
		{
			float4 packed = Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy );
			float3 normalTs = packed.rgb * 2 - 1;
			if ( g_flNormalRA > 0.5 )
			{
				normalTs.xy = packed.ra * 2 - 1;
				normalTs.z = sqrt( saturate( 1 - dot( normalTs.xy, normalTs.xy ) ) );
			}
			m.Normal = TransformNormal( normalize( normalTs ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		}
		#if S_RENDER_BACKFACES
			m.Normal *= i.face ? 1 : -1;
		#endif
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
