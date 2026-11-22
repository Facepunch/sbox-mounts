// Material calculations adapted from Overgrowth's GLSL; modified for s&box.
// Copyright 2022 Wolfire Games LLC. Apache-2.0; see Editor/Mounts/Overgrowth/LICENSE.
HEADER
{
	Description = "Overgrowth material inputs";
}
FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Transparency" );
	Feature( F_TRANSLUCENT, 0..1, "Transparency" );
	Feature( F_DETAIL, 0..1, "Detail layers" );
}
MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}
COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
	#include "common/shared.hlsl"
}
struct VertexInput
{
	#include "common/vertexinput.hlsl"
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float3 vNormalOs : TEXCOORD14;
	float4 vTangentOs : TEXCOORD15;
	float4 vTint : COLOR1;
};
VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		VS_DecodeObjectSpaceNormalAndTangent( v, i.vNormalOs, i.vTangentOs );
		i.vTint = GetExtraPerInstanceShaderData( v.nInstanceTransformID ).vTint;
		return FinalizeVertex( i );
	}
}
PS
{
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_DETAIL, F_DETAIL, Sys( ALL ) );
	#include "common/pixel.hlsl"
	BoolAttribute( renderbackfaces, F_RENDER_BACKFACES != 0 );
	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sClamp < Filter( ANISO ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	CreateInputTexture2D( ColorMap, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( NormalMap, Linear, 8, "None", "_normal", ",0/,0/0", Default4( 0.5, 0.5, 1, 0 ) );
	CreateInputTexture2D( PaletteMap, Linear, 8, "None", "_mask", ",0/,0/0", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( TranslucencyMap, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 0, 0, 0, 0 ) );
	Texture2D g_tColor < Channel( RGBA, Box( ColorMap ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	Texture2D g_tNormal < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tPalette < Channel( RGBA, Box( PaletteMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tTranslucency < Channel( RGBA, Box( TranslucencyMap ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	float g_flKind < Default( 0 ); >;
	float g_flTangentSpace < Default( 0 ); >;
	float g_flClampTexture < Default( 0 ); >;
	float g_flKeepSpec < Default( 0 ); >;
	float g_flMetalness < Default( 0 ); >;
	float g_flInvisible < Default( 0 ); >;
	float g_flScrollSpeed < Default( 0 ); >;
	float g_flMagma < Default( 0 ); >;
	float g_flAxisBlend < Default( 0 ); >;
	float g_flPalette < Default( 0 ); >;
	float g_flTranslucency < Default( 0 ); >;
	float3 g_vColorTint < Default3( 1, 1, 1 ); >;
	float3 g_vPalette0 < Default3( 1, 1, 1 ); >;
	float3 g_vPalette1 < Default3( 1, 1, 1 ); >;
	float3 g_vPalette2 < Default3( 1, 1, 1 ); >;
	float3 g_vPalette3 < Default3( 1, 1, 1 ); >;
	float3 g_vPalette4 < Default3( 1, 1, 1 ); >;

	#if S_DETAIL
		CreateInputTexture2D( WeightMap, Linear, 8, "None", "_mask", ",0/,0/0", Default4( 1, 0, 0, 0 ) );
		Texture2D g_tWeight < Channel( RGBA, Box( WeightMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
		CreateInputTexture2D( DetailColor0, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
		CreateInputTexture2D( DetailColor1, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
		CreateInputTexture2D( DetailColor2, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
		CreateInputTexture2D( DetailColor3, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
		CreateInputTexture2D( DetailNormal0, Linear, 8, "None", "_normal", ",0/,0/0", Default4( 0.5, 0.5, 1, 0 ) );
		CreateInputTexture2D( DetailNormal1, Linear, 8, "None", "_normal", ",0/,0/0", Default4( 0.5, 0.5, 1, 0 ) );
		CreateInputTexture2D( DetailNormal2, Linear, 8, "None", "_normal", ",0/,0/0", Default4( 0.5, 0.5, 1, 0 ) );
		CreateInputTexture2D( DetailNormal3, Linear, 8, "None", "_normal", ",0/,0/0", Default4( 0.5, 0.5, 1, 0 ) );
		Texture2D g_tDetailColor0 < Channel( RGBA, Box( DetailColor0 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
		Texture2D g_tDetailColor1 < Channel( RGBA, Box( DetailColor1 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
		Texture2D g_tDetailColor2 < Channel( RGBA, Box( DetailColor2 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
		Texture2D g_tDetailColor3 < Channel( RGBA, Box( DetailColor3 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
		Texture2D g_tDetailNormal0 < Channel( RGBA, Box( DetailNormal0 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
		Texture2D g_tDetailNormal1 < Channel( RGBA, Box( DetailNormal1 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
		Texture2D g_tDetailNormal2 < Channel( RGBA, Box( DetailNormal2 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
		Texture2D g_tDetailNormal3 < Channel( RGBA, Box( DetailNormal3 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
		float4 g_vAverage0 < Default4( 1, 1, 1, 1 ); >;
		float4 g_vAverage1 < Default4( 1, 1, 1, 1 ); >;
		float4 g_vAverage2 < Default4( 1, 1, 1, 1 ); >;
		float4 g_vAverage3 < Default4( 1, 1, 1, 1 ); >;
		float4 g_vDetailScale < Default4( 1, 1, 1, 1 ); >;
	#endif

	float4 SampleMap( Texture2D map, float2 uv )
	{
		if ( g_flClampTexture > 0.5 ) return map.Sample( g_sClamp, uv );
		return map.Sample( g_sWrap, uv );
	}
	float3 UnpackTangent( float4 value )
	{
		return normalize( float3( value.rg * 2 - 1, value.b ) );
	}
	float3 ObjectToTangent( float3 normal, PixelInput i )
	{
		float3 n = normalize( i.vNormalOs );
		float3 t = normalize( i.vTangentOs.xyz );
		float3 b = cross( n, t ) * i.vTangentOs.w;
		return float3( dot( normal, t ), dot( normal, b ), dot( normal, n ) );
	}
	float3 TangentToWorld( float3 normal, PixelInput i )
	{
		return normalize( normal.x * i.vTangentUWs + normal.y * i.vTangentVWs + normal.z * i.vNormalWs );
	}
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		if ( g_flInvisible > 0.5 ) discard;
		float2 uv = i.vTextureCoords.xy + float2( 0, g_flTime * g_flScrollSpeed );
		float4 color = SampleMap( g_tColor, uv );
		float4 normal = SampleMap( g_tNormal, uv );
		float3 baseNormal = g_flTangentSpace > 0.5 ? UnpackTangent( normal )
			: ObjectToTangent( normalize( float3( 1 - 2 * normal.g, 2 * normal.r - 1, 2 * normal.b - 1 ) ), i );
		float3 worldNormal = TangentToWorld( baseNormal, i );
		float3 tint = g_vColorTint * i.vTint.rgb;

		#if S_DETAIL
			float4 weights = SampleMap( g_tWeight, uv );
			weights.w = max( 0, 1 - weights.x - weights.y - weights.z );
			float3 world = (i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz) / 40;
			float2 uv0 = uv * g_vDetailScale.x;
			float2 uv1 = uv * g_vDetailScale.y;
			float2 uv2 = uv * g_vDetailScale.z;
			float2 uv3 = uv * g_vDetailScale.w;
			if ( g_flAxisBlend > 0.5 )
			{
				weights = float4( worldNormal.y * worldNormal.y, pow( max( 0, worldNormal.z ), 2 ),
					worldNormal.x * worldNormal.x, max( 0, -worldNormal.z ) );
				uv0 = world.xz * float2( 0.15, -0.15 );
				uv1 = world.yx * float2( 0.15, -0.15 );
				uv2 = world.yz * float2( 0.15, -0.15 );
				uv3 = uv1;
			}
			weights /= dot( weights, float4( 1, 1, 1, 1 ) );
			float fade = saturate( length( world * 40 - g_vCameraPositionWs ) / (200 * 40) );
			float4 detailColor = SampleMap( g_tDetailColor0, uv0 ) * weights.x
				+ SampleMap( g_tDetailColor1, uv1 ) * weights.y
				+ SampleMap( g_tDetailColor2, uv2 ) * weights.z
				+ SampleMap( g_tDetailColor3, uv3 ) * weights.w;
			float4 detailNormal = SampleMap( g_tDetailNormal0, uv0 ) * weights.x
				+ SampleMap( g_tDetailNormal1, uv1 ) * weights.y
				+ SampleMap( g_tDetailNormal2, uv2 ) * weights.z
				+ SampleMap( g_tDetailNormal3, uv3 ) * weights.w;
			float4 average = g_vAverage0 * weights.x + g_vAverage1 * weights.y + g_vAverage2 * weights.z + g_vAverage3 * weights.w;
			if ( g_flKeepSpec < 0.5 ) color.a = 0;
			color = lerp( detailColor * color / max( average, 0.01 ), color, fade );
			float3 detail = lerp( UnpackTangent( detailNormal ), float3( 0, 0, 1 ), fade );
			float3 b = normalize( cross( baseNormal, float3( 1, 0, 0 ) ) );
			float3 t = normalize( cross( b, baseNormal ) );
			worldNormal = TangentToWorld( detail.x * t + detail.y * b + detail.z * baseNormal, i );
			color.rgb *= lerp( float3( 1, 1, 1 ), tint, normal.a );
			color.a = saturate( color.a );
		#else
			color.rgb *= g_flKind == 2 ? lerp( float3( 1, 1, 1 ), tint, normal.a ) : tint;
		#endif

		if ( g_flPalette > 0.5 )
		{
			float4 palette = SampleMap( g_tPalette, uv );
			float other = max( 0, 1 - palette.x - palette.y - palette.z - palette.w );
			color.rgb *= palette.x * g_vPalette0 + palette.y * g_vPalette1 + palette.z * g_vPalette2 + palette.w * g_vPalette3 + other * g_vPalette4;
		}
		Material m = Material::Init();
		m.Albedo = color.rgb;
		m.Normal = worldNormal;
		m.Opacity = i.vTint.a;
		#if S_ALPHA_TEST || S_TRANSLUCENT
			m.Opacity *= color.a;
		#endif
		m.Roughness = lerp( 0.7, 1, pow( max( 0, dot( color.rgb, float3( 1, 1, 1 ) ) / 3 ), 0.01 ) );
		if ( g_flKind == 1 ) m.Roughness = pow( 1 - pow( saturate( color.a ), 2.2 ), 20 );
		if ( g_flKind == 2 ) m.Roughness = normal.a;
		if ( g_flKeepSpec > 0.5 || g_flMetalness > 0.5 ) m.Roughness = 1 - normal.a;
		m.Metalness = g_flMetalness > 0.5 ? color.a : 0;
		m.Transmission = g_flTranslucency > 0.5 ? SampleMap( g_tTranslucency, uv ).rgb : float3( 0, 0, 0 );
		if ( g_flMagma == 1 )
		{
			float3 emission = SampleMap( g_tColor, uv + float2( 0, g_flTime * 0.3 ) ).rgb
				* (pow( SampleMap( g_tNormal, uv + float2( 0, g_flTime * 0.5 ) ).a, 2.2 ) + 0.1) * 2;
			m.Albedo = 0;
			m.Emission = emission;
		}
		m.AmbientOcclusion = 1;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;
		return ShadingModelStandard::Shade( i, m );
	}
}
