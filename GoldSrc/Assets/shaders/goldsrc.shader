
HEADER
{
	Description = "";
}

FEATURES
{
	#include "common/features.hlsl"
	
	Feature( F_CHROME, 0..1, "Chrome" );
	Feature( F_FULLBRIGHT, 0..1, "Fullbright" );
	Feature( F_ADDITIVE, 0..1, "Additive" );
	Feature( F_ALPHA_TEST, 0..1, "Alpha Test" );
}

MODES
{
    Forward();
    Depth();
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
	#include "goldsrc_shading.hlsl"
	
	SamplerState g_sSampler0 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

	StaticCombo( S_CHROME, F_CHROME, Sys( ALL ) )
	StaticCombo( S_FULLBRIGHT, F_FULLBRIGHT, Sys( ALL ) )
	StaticCombo( S_ADDITIVE, F_ADDITIVE, Sys( ALL ) )
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) )

	#if ( S_ADDITIVE )
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, ONE );
		RenderState( DstBlend, ONE );
		RenderState( BlendOp, ADD );
		RenderState( DepthWriteEnable, false );
	#endif
	
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );

		float4 color = Tex2DS( g_tColor, g_sSampler0, i.vTextureCoords.xy );

		#if ( S_ALPHA_TEST )
			clip( color.a - 0.5 );
		#endif

		// GoldSrc's default texture upload uses texgamma 2.0 / gamma 2.5.
		color.rgb = SrgbGammaToLinear( pow( SrgbLinearToGamma( color.rgb ), 0.8 ) );
		m.Albedo = color.rgb;
		m.Normal = normalize( i.vNormalWs );
        m.TextureCoords = i.vTextureCoords.xy;
        m.Roughness = 1;
        m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = 1;
		m.Emission = 0;
		m.Transmission = 0;
		m.WorldPositionWithOffset = i.vPositionWithOffsetWs;
		m.WorldPosition = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		m.ScreenPosition = i.vPositionSs;
		#if !S_MODE_DEPTH
			Decals::Apply( m.WorldPosition, m );
		#endif

		#if ( S_FULLBRIGHT || S_ADDITIVE )
			m.Emission = m.Albedo;
			m.Albedo = 0;
			m.Metalness = 0;
			m.Roughness = 1;
		#endif

		return GoldSrcShade( i, m );
	}
}
