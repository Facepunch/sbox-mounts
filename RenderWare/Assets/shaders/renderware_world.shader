
HEADER
{
	Description = "Renderware batched world geometry, each vertex samples its own texture bindlessly";
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

	// the texture's bindless index (see WorldTextures.cs)
	nointerpolation float vTextureIndex : TEXCOORD9;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vTextureIndex = v.vTexCoord2.x;

		// prelit vertex colours: the map's baked lighting, stored gamma space
		i.vVertexColor = v.vColor.rgba;
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
	#include "common/classes/Bindless.hlsl"

	// the cutout copy of the world material turns this on (see WorldLoader)
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	RenderState( CullMode, NONE );

	SamplerState g_sWrap < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;

	// every vertex says which texture it uses - one material for the whole world, whatever it's textured with
	float4 SampleWorld( float2 uv, float index )
	{
		return Bindless::GetTexture2DSrgb( (int)( index + 0.5 ) ).Sample( g_sWrap, uv );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );

		float4 color = SampleWorld( i.vTextureCoords.xy, i.vTextureIndex );

		// blended modes are unlit like the game drew them: texture * baked vertex colour, nothing from the sun
		#if S_ADDITIVE_BLEND || S_ALPHA_BLEND
			#if S_MODE_DEPTH
				discard; // see-through - doesn't block the prepass or cast shadows
			#endif
			#if S_ADDITIVE_BLEND
				return float4( color.rgb, color.a * i.vVertexColor.a );
			#else
				return float4( color.rgb * i.vVertexColor.rgb, color.a * i.vVertexColor.a );
			#endif
		#endif

		#if S_ALPHA_TEST
			clip( color.a - 0.5 );
		#endif

		float3 prelight = lerp( 0.1, 1.0, i.vVertexColor.rgb );
		m.Albedo = color.rgb * prelight;
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
