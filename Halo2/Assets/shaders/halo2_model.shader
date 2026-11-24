
HEADER
{
	Description = "Halo 2 shader_model material";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_MASKED, 0..1, "Alpha tested" );
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

	StaticCombo( S_MASKED, F_MASKED, Sys( ALL ) );
	#if S_MASKED
		BoolAttribute( alphatest, true );
	#endif
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

	CreateInputTexture2D( Detail, Linear, 8, "None", "", "", Default3( 0.5, 0.5, 0.5 ) );
	Texture2D g_tDetail < Channel( RGB, Box( Detail ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	CreateInputTextureCube( Environment, Srgb, 8, "", "", "", Default3( 0, 0, 0 ) );
	CreateTextureCube( g_tEnvironment ) < Channel( RGB, GGXCubeMapBlur( Environment ), Srgb ); OutputFormat( DXT1 ); SrgbRead( true ); >;
	CreateInputTexture2D( Glow, Srgb, 8, "None", "", "", Default3( 0, 0, 0 ) );
	Texture2D g_tGlow < Channel( RGB, Box( Glow ), Srgb ); OutputFormat( DXT1 ); SrgbRead( true ); >;
	float3 g_vGlow < Default3( 0, 0, 0 ); >;
	float4 g_vDetailTransform < Default4( 0, 0, 0, 0 ); >;
	float g_flEnvironmentUnsignedZ < Default( 0 ); >;
	float4 g_vEnvironmentFacing < Default4( 0, 0, 0, 0 ); >;
	float4 g_vEnvironmentGlancing < Default4( 0, 0, 0, 0 ); >;
	#if S_CHANGE_COLOR
		float3 g_vPrimaryColor < Default3( 1, 1, 1 ); >;
		float3 g_vSecondaryColor < Default3( 1, 1, 1 ); >;
		float3 g_vTertiaryColor < Default3( 1, 1, 1 ); >;
		float3 g_vQuaternaryColor < Default3( 1, 1, 1 ); >;
	#endif

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );

		float4 baseColor = Tex2DS( g_tBaseMap, g_sSampler0, i.vTextureCoords.xy );
		float3 albedo = baseColor.rgb;
		if ( any( g_vDetailTransform.xy ) )
			albedo *= 2.0 * Tex2DS( g_tDetail, g_sSampler0, i.vTextureCoords.xy * g_vDetailTransform.xy + g_vDetailTransform.zw ).rgb;

		#if S_CHANGE_COLOR
		{
			// H2 change_color_map: R = primary mask, G = secondary mask
			float4 ccMask = Tex2DS( g_tChangeColorMap, g_sSampler0, i.vTextureCoords.xy );
			float3 tintColor = i.vTintColor.rgb;

			// Object change colors occupy independent mask channels.
			albedo *= lerp( float3( 1, 1, 1 ), g_vPrimaryColor * tintColor, ccMask.r );
			albedo *= lerp( float3( 1, 1, 1 ), g_vSecondaryColor * tintColor, ccMask.g );
			albedo *= lerp( 1.0.xxx, g_vTertiaryColor, ccMask.b );
			albedo *= lerp( 1.0.xxx, g_vQuaternaryColor, ccMask.a );
		}
		#endif

		// The imported tangent frame uses the authored Halo bitangent.
		float4 texNormal = Tex2DS( g_tNormalMap, g_sSampler0, i.vTextureCoords.xy );
		#if S_MASKED
			clip( texNormal.a - 0.5 );
		#endif
		texNormal.y = 1.0 - texNormal.y;
		float3 normal = TransformNormal( DecodeNormal( texNormal.xyz ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		float specularMask = baseColor.a;

		m.Albedo = albedo;
		m.Normal = normal;
		m.Opacity = 1.0;
		m.Roughness = 1.0 - ( specularMask * 0.8 );
		m.Metalness = specularMask * 0.3;
		m.AmbientOcclusion = 1.0;
		m.Emission = any( g_vGlow ) ? g_vGlow * Tex2DS( g_tGlow, g_sSampler0, i.vTextureCoords.xy ).rgb : 0;
		if ( g_vEnvironmentFacing.w + g_vEnvironmentGlancing.w > 0 )
		{
			float3 view = CalculatePositionToCameraDirWs( m.WorldPosition );
			float3 reflectionNormal = normal;
			if ( g_flEnvironmentUnsignedZ )
			{
				// MCC default_environment_map_tangent_space expands XY and retains the sampled Z.
				float3 ts = float3( texNormal.xy * 2.0 - 1.0, texNormal.z );
				reflectionNormal = TransformNormal( ts, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
			}
			float grazing = 1 - saturate( dot( view, reflectionNormal ) );
			float4 environment = lerp( g_vEnvironmentFacing, g_vEnvironmentGlancing, grazing );
			m.Emission += TexCube( g_tEnvironment, reflect( -view, reflectionNormal ) ).rgb * environment.rgb * environment.w * specularMask;
		}
		m.TintMask = 0;
		m.Transmission = 0;
		m.TextureCoords = i.vTextureCoords.xy;

		return ShadingModelStandard::Shade( i, m );
	}
}
