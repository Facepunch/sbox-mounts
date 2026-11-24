
HEADER
{
	Description = "Halo 2 shader_model material";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_MASKED, 0..1, "Alpha tested" );
	Feature( F_CHANGE_COLOR, 0..3( 0="None", 1="One", 2="Two", 3="Four" ), "Change Colors" );
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
		return FinalizeVertex( ProcessVertex( v ) );
	}
}

PS
{
	StaticCombo( S_ALPHA_TEST, F_MASKED, Sys( ALL ) );
	StaticCombo( S_CHANGE_COLOR, F_CHANGE_COLOR, Sys( ALL ) );
	DynamicCombo( D_OPAQUE_FADE, 0..1, Sys( ALL ) );
	DynamicComboRule( Allow1( S_ALPHA_TEST, D_OPAQUE_FADE ) );

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

	#define MATERIAL_ALPHA_TEXTURE g_tNormalMap
	#include "common/pixel.hlsl"

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );

		float4 baseColor = Tex2DS( g_tBaseMap, g_sAniso, i.vTextureCoords.xy );
		float3 albedo = baseColor.rgb;
		if ( any( g_vDetailTransform.xy ) )
			albedo *= 2.0 * Tex2DS( g_tDetail, g_sAniso, i.vTextureCoords.xy * g_vDetailTransform.xy + g_vDetailTransform.zw ).rgb;

		#if S_CHANGE_COLOR
		{
			float4 mask = Tex2DS( g_tChangeColorMap, g_sAniso, i.vTextureCoords.xy );
			albedo *= lerp( 1.0.xxx, g_vPrimaryColor, mask.r );
			#if S_CHANGE_COLOR >= 2
				albedo *= lerp( 1.0.xxx, g_vSecondaryColor, mask.g );
			#endif
			#if S_CHANGE_COLOR == 3
				albedo *= lerp( 1.0.xxx, g_vTertiaryColor, mask.b );
				albedo *= lerp( 1.0.xxx, g_vQuaternaryColor, mask.a );
			#endif
		}
		#endif

		// The imported tangent frame uses the authored Halo bitangent.
		float4 texNormal = Tex2DS( g_tNormalMap, g_sAniso, i.vTextureCoords.xy );
		#if S_ALPHA_TEST
			m.Opacity = texNormal.a * i.vVertexColor.a;
		#elif D_OPAQUE_FADE
			m.Opacity = i.vVertexColor.a;
			if ( g_nMSAASampleCount == 1 )
				OpaqueFadeDepth( (m.Opacity + 0.5 + 1.0 / 255.0) * 0.5, i.vPositionSs.xy );
		#endif
		texNormal.y = 1.0 - texNormal.y;
		m.TangentNormal = DecodeNormal( texNormal.xyz );
		m.Normal = TransformNormal( m.TangentNormal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		// Halo uses diffuse alpha as reflection strength. It carries no metalness or gloss exponent.
		float reflectionStrength = baseColor.a;

		m.Albedo = albedo * i.vVertexColor.rgb;
		m.Emission = any( g_vGlow ) ? g_vGlow * Tex2DS( g_tGlow, g_sAniso, i.vTextureCoords.xy ).rgb : 0;
		if ( g_vEnvironmentFacing.w + g_vEnvironmentGlancing.w > 0 )
		{
			float3 view = CalculatePositionToCameraDirWs( m.WorldPosition );
			float3 reflectionNormal = m.Normal;
			if ( g_flEnvironmentUnsignedZ )
			{
				// MCC default_environment_map_tangent_space expands XY and retains the sampled Z.
				float3 ts = float3( texNormal.xy * 2.0 - 1.0, texNormal.z );
				reflectionNormal = TransformNormal( ts, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
			}

			// The baked reflection cube describes the armor, while the scene probe supplies
			// the incident light at the model. Only illumination maps emit in darkness.
			float3 lightmap = saturate( AmbientLight::From( m.WorldPosition, m.ScreenPosition, m.Normal ) );
			float lightMask = saturate( dot( lightmap, (192.0 / 255.0).xxx ) );
			float reflectionMask = saturate( 2.0 * reflectionStrength * lightMask );
			float grazing = 1 - saturate( dot( view, reflectionNormal ) );
			float4 environment = lerp( g_vEnvironmentFacing, g_vEnvironmentGlancing, grazing );
			m.Emission += TexCube( g_tEnvironment, reflect( -view, reflectionNormal ) ).rgb * environment.rgb * environment.w * reflectionMask;
		}

		return ShadingModelStandard::Shade( m );
	}
}
