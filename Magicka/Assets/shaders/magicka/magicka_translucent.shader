HEADER
{
	Description = "Magicka alpha blended material";
}

FEATURES
{
	#include "common/features.hlsl"

}

MODES
{
	Forward();
}

COMMON
{
	#define S_TRANSLUCENT 1
	#define CUSTOM_MATERIAL_INPUTS
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
	#include "common/pixel.hlsl"

	// Alpha-blended magic: the texture's alpha is coverage, as Magicka's alpha passes use it.
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
	RenderState( BlendOp, ADD );

	SamplerState g_sSampler0 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

	CreateInputTexture2D( Detail, Srgb, 8, "None", "_detail", ",0/,0/0", Default4( 0.00, 0.00, 0.00, 0.00 ) );
	Texture2D g_tDetail < Channel( RGBA, Box( Detail ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

	CreateInputTexture2D( Material, Linear, 8, "None", "_material", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tMaterial < Channel( RGBA, Box( Material ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;

	float3 g_vTint < Default3( 1.0, 1.0, 1.0 ); >;
	float g_flEmission < Default1( 0.0 ); >;
	float g_flAlphaTest < Default1( 0.0 ); >;
	float g_flOpacity < Default1( 1.0 ); >;
	float g_flSpecAmount < Default1( 0.0 ); >;
	float g_flSpecPower < Default1( 32.0 ); >;

	// Magicka lights characters with a scene rim light (0.8 glow, 1.5 power, 0.2 bias by default).
	float g_flRimGlow < Default1( 0.0 ); >;
	float g_flRimPower < Default1( 1.5 ); >;
	float g_flRimBias < Default1( 0.0 ); >;
	float g_flDetailAlpha < Default1( 0.0 ); >;
	float g_flHasMaterialMap < Default1( 0.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );

		float4 base = Tex2DS( g_tColor, g_sSampler0, i.vTextureCoords.xy );
		float4 detail = Tex2DS( g_tDetail, g_sSampler0, i.vTextureCoords.xy );
		float3 spg = g_flHasMaterialMap > 0.5
			? Tex2DS( g_tMaterial, g_sSampler0, i.vTextureCoords.xy ).rgb
			: float3( 1.0, 1.0, 1.0 );

		float detailA = detail.a * g_flDetailAlpha;
		float baseA = base.a;
		float outA = detailA + baseA * ( 1.0 - detailA );

		clip( outA - g_flAlphaTest );

		float3 baseRgb = base.rgb * i.vVertexColor.rgb;
		float3 combined = outA > 0.0001
			? ( detail.rgb * detailA + baseRgb * ( baseA * ( 1.0 - detailA ) ) ) / outA
			: float3( 0.0, 0.0, 0.0 );
		float3 albedo = combined * g_vTint;

		// SPG texture: R = Specular amount, G = Specular power, B = Glow (emissive).
		float specAmount = g_flSpecAmount * spg.r;
		float specPower = g_flSpecPower * spg.g;
		float gloss = saturate( specPower / 32.0 );
		float roughness = lerp( 0.92, 0.12, saturate( specAmount ) * gloss );

		float3 viewDir = normalize( g_vCameraPositionWs - i.vPositionWithOffsetWs.xyz );
		float facing = saturate( dot( normalize( i.vNormalWs ), viewDir ) );
		float fresnel = pow( 1.0 - facing, max( g_flRimPower, 0.001 ) );
		float rim = saturate( fresnel * g_flRimGlow + g_flRimBias ) * step( 0.0001, g_flRimGlow );

		m.Albedo = albedo;
		m.Normal = i.vNormalWs;
		m.TextureCoords = i.vTextureCoords.xy;
		m.Roughness = roughness;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = outA * g_flOpacity;
		m.Emission = albedo * ( g_flEmission * spg.b ) + albedo * rim;
		m.Transmission = 0;

		return ShadingModelStandard::Shade( i, m );
	}
}
