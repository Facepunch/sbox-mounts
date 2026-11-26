HEADER
{
	Description = "Magicka opaque model and level material";
}

FEATURES
{
	#include "common/features.hlsl"

}

MODES
{
	Forward();
	Depth();
}

COMMON
{
	#define S_UV2 1
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
	float2 vTexCoord2 : TEXCOORD10;
	float4 vMeshColor : COLOR0;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vTexCoord2 = v.vTexCoord2;
		i.vMeshColor = v.vColor;
		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"

	// Keep the pixel shader in depth/shadow passes so alpha-tested cutouts clip there too.
	BoolAttribute( alphatest, true );

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
	float g_flDualLayer < Default1( 0.0 ); >;
	float g_flLayer0AlphaMask < Default1( 0.0 ); >;
	float g_flLayer1AlphaMask < Default1( 0.0 ); >;
	float g_flSharpness < Default1( 1.0 ); >;
	float g_flHasMaterialMap < Default1( 0.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );

		float4 base = Tex2DS( g_tColor, g_sSampler0, i.vTextureCoords.xy );
		float3 spg = g_flHasMaterialMap > 0.5
			? Tex2DS( g_tMaterial, g_sSampler0, i.vTextureCoords.xy ).rgb
			: float3( 1.0, 1.0, 1.0 );
		float3 albedo;

		if ( g_flDualLayer > 0.5 )
		{
			// Magicka RenderDeferredEffect dual-layer blending: vertex alpha 0 = Layer0, 1 = Layer1.
			float2 uv1 = ( dot( i.vTexCoord2, i.vTexCoord2 ) > 0.00001 ) ? i.vTexCoord2 : i.vTextureCoords.xy;
			float4 layer1 = Tex2DS( g_tDetail, g_sSampler0, uv1 );
			float blend = i.vMeshColor.a;
			if ( g_flLayer0AlphaMask > 0.5 )
			{
				// Layer0's alpha is a height/blend mask (e.g. snow over forest floor).
				// Magicka's vertex alpha typically stops around 0.1..0.85 at mesh boundaries, so a
				// steeper smoothstep ensures snow fades completely to 0 before the polygon edge.
				float x = ( blend * 2.0 - 1.0 ) + ( ( 1.0 - base.a ) - 0.5 );
				blend = smoothstep( 0.0, 1.0, x * max( g_flSharpness * 3.5, 3.0 ) + 0.5 );
			}
			else if ( g_flLayer1AlphaMask > 0.5 )
			{
				// Layer1's alpha is a height/blend mask (e.g. stone tiles over dirt).
				float x = ( blend * 2.0 - 1.0 ) + ( layer1.a - 0.5 );
				blend = smoothstep( 0.0, 1.0, x * max( g_flSharpness * 3.5, 3.0 ) + 0.5 );
			}
			else
			{
				float outA = lerp( base.a, layer1.a, blend );
				clip( outA - g_flAlphaTest );
			}
			albedo = lerp( base.rgb, layer1.rgb, blend ) * g_vTint * i.vVertexColor.rgb * i.vMeshColor.rgb;
		}
		else
		{
			float4 detail = Tex2DS( g_tDetail, g_sSampler0, i.vTextureCoords.xy );

			// Magicka tints only the base robe/body layer (DiffuseMap1); detail trim/hands (DiffuseMap0) stay untinted.
			float detailA = detail.a * g_flDetailAlpha;
			float baseA = base.a;
			float outA = detailA + baseA * ( 1.0 - detailA );

			clip( outA - g_flAlphaTest );

			float3 baseRgb = base.rgb * i.vVertexColor.rgb;
			float3 combined = outA > 0.0001
				? ( detail.rgb * detailA + baseRgb * ( baseA * ( 1.0 - detailA ) ) ) / outA
				: float3( 0.0, 0.0, 0.0 );
			albedo = combined * g_vTint;
		}

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
		m.Opacity = g_flOpacity;
		m.Emission = albedo * ( g_flEmission * spg.b ) + albedo * rim;
		m.Transmission = 0;

		return ShadingModelStandard::Shade( i, m );
	}
}
