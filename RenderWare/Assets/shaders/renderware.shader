
HEADER
{
	Description = "";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Rendering" );
	Feature( F_ALPHA_BLEND, 0..1, "Rendering" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH ); // our own PS in the depth pass, so cutouts clip in the prepass and shadows too
}

COMMON
{
	#include "common/shared.hlsl"

	#define CUSTOM_MATERIAL_INPUTS

	// GTA renders everything with culling off (rwCULLMODECULLNONE), lots of geometry is single faced
	// and relies on it. Defining this also gives PixelInput the SV_IsFrontFace input we flip normals with.
	#define S_RENDER_BACKFACES 1
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
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

		// prelit vertex colours: the map's baked lighting, stored gamma space
		i.vVertexColor = float4( pow( v.vColor.rgb, 2.2 ), v.vColor.a );

		return FinalizeVertex( i );
	}
}

PS
{
	// additive (IDE flag 0x8 - night windows, light beams, glows): blended on top in the translucent pass, unlit
	StaticCombo( S_ADDITIVE_BLEND, F_ADDITIVE_BLEND, Sys( ALL ) );

	// alpha blended (IDE flag 0x4 "draw last" with soft alpha - light beams, glass): unlit, faded by texture * vertex alpha
	StaticCombo( S_ALPHA_BLEND, F_ALPHA_BLEND, Sys( ALL ) );

	// either blended mode is translucent: common/pixel.hlsl (sbox_pixel.fxc) then puts it in the translucent pass
	// and sets SRC_ALPHA onto ONE (additive) or INV_SRC_ALPHA, with no depth writes
	#if S_ADDITIVE_BLEND || S_ALPHA_BLEND
		#define S_TRANSLUCENT 1
	#endif

	#include "common/pixel.hlsl"

	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	RenderState( CullMode, NONE );

	SamplerState g_sSampler0 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D  g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );

		float4 color = g_tColor.Sample( g_sSampler0, i.vTextureCoords.xy );

		// blended modes are unlit like the game drew them: texture * baked vertex colour, nothing from the sun
		#if S_ADDITIVE_BLEND || S_ALPHA_BLEND
			#if S_MODE_DEPTH
				discard; // see-through - doesn't block the prepass or cast shadows
			#endif

			#if S_ALPHA_BLEND
				m.Albedo = 0;
				m.Emission = color.rgb * i.vVertexColor.rgb;
				m.Opacity = color.a * i.vVertexColor.a;
				return ShadingModelStandard::Shade( i, m );
			#else // S_ADDITIVE_BLEND
				return float4( color.rgb, color.a * i.vVertexColor.a );
			#endif
		#endif

		#if S_ALPHA_TEST
			clip( color.a - 0.5 );
		#endif

		m.Albedo = color.rgb * i.vVertexColor.rgb; // texture * prelighting
		m.Normal = i.vNormalWs * ( i.face ? 1.0 : -1.0 ); // back faces get lit from their own side
		m.TextureCoords = i.vTextureCoords.xy;
		m.Roughness = 1;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = 1;
		m.Emission = 0;
		m.Transmission = 0;

		return ShadingModelStandard::Shade( i, m );
	}
}
