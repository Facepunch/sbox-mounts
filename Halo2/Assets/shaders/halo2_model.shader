
HEADER
{
	Description = "Halo 2 shader_model material";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_CHANGE_COLOR, 0..1, "Change Color Map" );
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
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vTintColor : COLOR1;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );

		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;

		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"

	StaticCombo( S_CHANGE_COLOR, F_CHANGE_COLOR, Sys( ALL ) );

	SamplerState g_sSampler0 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( BaseMap, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tBaseMap < Channel( RGBA, Box( BaseMap ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

	CreateInputTexture2D( NormalMap, Linear, 8, "NormalizeNormals", "_normal", ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;

	#if S_CHANGE_COLOR
		CreateInputTexture2D( ChangeColorMap, Linear, 8, "None", "_cc", ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
		Texture2D g_tChangeColorMap < Channel( RGBA, Box( ChangeColorMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	#endif

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );

		float4 baseColor = Tex2DS( g_tBaseMap, g_sSampler0, i.vTextureCoords.xy );
		float3 albedo = baseColor.rgb;

		#if S_CHANGE_COLOR
		{
			// H2 change_color_map: R = primary mask, G = secondary mask
			float4 ccMask = Tex2DS( g_tChangeColorMap, g_sSampler0, i.vTextureCoords.xy );
			float3 tintColor = i.vTintColor.rgb;

			// Apply tint to both primary (R) and secondary (G) masked regions
			albedo *= lerp( float3( 1, 1, 1 ), tintColor, ccMask.r );
			albedo *= lerp( float3( 1, 1, 1 ), tintColor, ccMask.g );
		}
		#endif

		// Normal mapping + specular from bump alpha (H2 uses DirectX normals, flip Y)
		float4 texNormal = Tex2DS( g_tNormalMap, g_sSampler0, i.vTextureCoords.xy );
		texNormal.y = 1.0 - texNormal.y;
		float3 normal = TransformNormal( DecodeNormal( texNormal.xyz ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		float specularMask = texNormal.a;

		m.Albedo = albedo;
		m.Normal = normal;
		m.Opacity = 1.0;
		m.Roughness = 1.0 - ( specularMask * 0.8 );
		m.Metalness = specularMask * 0.3;
		m.AmbientOcclusion = 1.0;
		m.Emission = float3( 0, 0, 0 );
		m.TintMask = 0;
		m.Transmission = 0;
		m.TextureCoords = i.vTextureCoords.xy;

		return ShadingModelStandard::Shade( i, m );
	}
}
