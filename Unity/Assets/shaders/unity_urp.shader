HEADER
{
	Description = "Unity URP / Standard / Decal / Unlit mounted game material (lean fast path)";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Translucent" );
	Feature( F_TRANSLUCENT, 0..1, "Translucent" );
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
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vMeshColor : TEXCOORD8;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vMeshColor = v.vColor;
		return FinalizeVertex( i );
	}
}

PS
{
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );

	#include "common/utils/Material.CommonInputs.hlsl"
	#include "common/pixel.hlsl"

	CreateInputTexture2D( TextureEmission, Srgb, 8, "", "_emiss", "Material,10/80", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( TextureMetallic, Linear, 8, "", "_metal", "Material,10/81", Default4( 1.0, 1.0, 1.0, 1.0 ) );

	Texture2D g_tEmission < Channel( RGB, Box( TextureEmission ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tMetallic < Channel( RGBA, Box( TextureMetallic ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	float4 g_vBaseColor < Default4( 1.0, 1.0, 1.0, 1.0 ); >;
	float4 g_vMainTexST < Default4( 1.0, 1.0, 0.0, 0.0 ); >;
	float4 g_vEmissionColor < Default4( 0.0, 0.0, 0.0, 1.0 ); >;
	float g_flMetallic < Default( 0.0 ); >;
	float g_flSmoothness < Default( 0.25 ); >;
	float g_flBumpScale < Default( 1.0 ); >;
	float g_flCutoff < Default( 0.5 ); >;
	int g_bHasNormal < Default( 0 ); >;
	int g_bNormalDxt5nm < Default( 0 ); >;
	int g_bHasEmission < Default( 0 ); >;
	int g_bHasMetallicMap < Default( 0 ); >;
	int g_bUseVertexColor < Default( 0 ); >;
	int g_bPointFilter < Default( 0 ); >;
	int g_nShaderMode < Default( 0 ); >;

	SamplerState g_sPointMipLinear < Filter( MIN_MAG_POINT_MIP_LINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sUnityAniso < Filter( ANISOTROPIC ); MaxAniso( 4 ); AddressU( WRAP ); AddressV( WRAP ); >;

	float4 SampleFast( Texture2D tex, float2 uv )
	{
		return g_bPointFilter != 0
			? tex.Sample( g_sPointMipLinear, uv )
			: tex.Sample( g_sUnityAniso, uv );
	}

	float3 UnpackUnityNormal( float2 uv, float3 vNormalWs, float3 vTangentUWs, float3 vTangentVWs )
	{
		float4 s = SampleFast( g_tNormal, uv );
		float3 nTs;

		if ( g_bNormalDxt5nm != 0 )
		{
			nTs.xy = float2( s.a, s.g ) * 2.0 - 1.0;
		}
		else
		{
			nTs.xy = s.rg * 2.0 - 1.0;
		}

		nTs.xy *= g_flBumpScale;
		nTs.y = -nTs.y;
		nTs.z = sqrt( saturate( 1.0 - dot( nTs.xy, nTs.xy ) ) );

		return TransformNormal( nTs, vNormalWs, vTangentUWs, vTangentVWs );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );

		float2 uv = i.vTextureCoords.xy * g_vMainTexST.xy + g_vMainTexST.zw;
		float4 baseCol = SampleFast( g_tColor, uv ) * g_vBaseColor;

		if ( g_bUseVertexColor != 0 )
		{
			baseCol *= i.vMeshColor;
		}

		#if S_ALPHA_TEST
			clip( baseCol.a - g_flCutoff );
		#endif

		m.Albedo = baseCol.rgb;
		m.Opacity = baseCol.a;

		float metallic = g_flMetallic;
		float smoothness = g_flSmoothness;
		if ( g_bHasMetallicMap != 0 )
		{
			float4 ms = SampleFast( g_tMetallic, uv );
			metallic = ms.r;
			smoothness = ms.a * g_flSmoothness;
		}

		m.Metalness = saturate( metallic );
		m.Roughness = saturate( 1.0 - smoothness );
		m.AmbientOcclusion = 1.0;
		m.Transmission = 0.0;

		if ( g_nShaderMode == 6 )
		{
			m.Emission = baseCol.rgb;
			m.Albedo = float3( 0.0, 0.0, 0.0 );
		}
		else if ( g_bHasEmission != 0 )
		{
			m.Emission = SampleFast( g_tEmission, uv ).rgb * g_vEmissionColor.rgb;
		}
		else if ( dot( g_vEmissionColor.rgb, g_vEmissionColor.rgb ) > 0.0001 )
		{
			m.Emission = g_vEmissionColor.rgb;
		}

		float3 nWs = normalize( i.vNormalWs );
		if ( g_bHasNormal != 0 )
		{
			m.Normal = UnpackUnityNormal( uv, nWs, i.vTangentUWs, i.vTangentVWs );
		}
		else
		{
			m.Normal = nWs;
		}

		#if S_RENDER_BACKFACES
			if ( !i.face )
			{
				m.Normal = -m.Normal;
			}
		#endif

		m.TextureCoords = uv;
		return ShadingModelStandard::Shade( i, m );
	}
}
