HEADER
{
	Description = "Unity URP ShaderGraph Stylized Animated Water material";
}

FEATURES
{
	#include "common/features.hlsl"
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
		PixelInput i = ProcessVertex( v );
		return FinalizeVertex( i );
	}
}

PS
{
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );

	#include "common/utils/Material.CommonInputs.hlsl"
	#include "common/pixel.hlsl"

	float4 g_vBaseColor < Default4( 0.04, 0.31, 0.45, 0.75 ); >;
	float4 g_vWaterLightColor < Default4( 0.14, 0.70, 0.77, 1.0 ); >;
	float4 g_vLayerScales < Default4( 1.0, 1.0, 1.0, 1.0 ); >;
	float4 g_vMixParams < Default4( 1.0, 0.0, 0.04, 0.02 ); >;
	float g_flSmoothness < Default( 0.35 ); >;
	int g_nShaderMode < Default( 4 ); >;

	SamplerState g_sPointMipLinear < Filter( MIN_MAG_POINT_MIP_LINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );

		float tile = abs( g_vLayerScales.x ) > 1e-4 ? abs( g_vLayerScales.x ) : 1.0;
		float2 wUv;
		if ( g_nShaderMode == 4 )
		{
			float3 worldInches = i.vPositionWithOffsetWs.xyz + g_vCameraPositionWs.xyz;
			float3 unityPos = float3( -worldInches.y, worldInches.z, worldInches.x ) * 0.0254;
			wUv = float2( unityPos.x, -unityPos.z ) * ( tile * 0.25 );
		}
		else
		{
			wUv = i.vTextureCoords.xy * tile;
		}

		float2 scroll1 = float2( g_flTime * ( g_vMixParams.z + 0.06 ), g_flTime * ( g_vMixParams.w + 0.03 ) );
		float2 scroll2 = float2( -g_flTime * 0.04, g_flTime * 0.05 );
		float3 wSample1 = g_tColor.Sample( g_sPointMipLinear, wUv + scroll1 ).rgb;
		float3 wSample2 = g_tColor.Sample( g_sPointMipLinear, wUv * 1.15 + scroll2 ).rgb;
		float3 wPattern = pow( saturate( ( wSample1 * 0.65 + wSample2 * 0.35 ) * 1.4 ), 0.55 );

		float3 waterRgb = g_vBaseColor.rgb + wPattern * ( g_vWaterLightColor.rgb - g_vBaseColor.rgb );

		m.Albedo = waterRgb;
		m.Opacity = g_vBaseColor.a;
		m.Emission = waterRgb * 0.30;
		m.Metalness = 0.0;
		m.Roughness = saturate( 1.0 - g_flSmoothness );
		m.AmbientOcclusion = 1.0;
		m.Transmission = 0.0;

		float3 nWs = normalize( i.vNormalWs );
		#if S_RENDER_BACKFACES
			if ( !i.face )
			{
				nWs = -nWs;
			}
		#endif
		m.Normal = nWs;
		m.TextureCoords = wUv;
		return ShadingModelStandard::Shade( i, m );
	}
}
