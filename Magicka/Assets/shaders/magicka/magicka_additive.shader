HEADER
{
	Description = "Magicka additive effect material";
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

	// Magicka uses XNA's BlendState.Additive: SrcAlpha * colour + destination.
	// The texture's alpha is the glow's coverage, so the quad's edges fade out.
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, NONE );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );

	SamplerState g_sSampler0 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

	CreateInputTexture2D( Material, Srgb, 8, "None", "_material", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tMaterial < Channel( RGBA, Box( Material ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

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

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 color = Tex2DS( g_tColor, g_sSampler0, i.vTextureCoords.xy );
		float3 albedo = color.rgb * g_vTint * i.vVertexColor.rgb;
		float alpha = color.a * g_flOpacity * i.vVertexColor.a;

		// BlendState.Additive already multiplies this by alpha (the texture alpha).
		float3 additive = albedo * ( 1.0 + g_flEmission );

		float3 viewDir = normalize( g_vCameraPositionWs - i.vPositionWithOffsetWs.xyz );
		float facing = saturate( dot( normalize( i.vNormalWs ), viewDir ) );
		float fresnel = pow( 1.0 - facing, max( g_flRimPower, 0.001 ) );
		float rim = saturate( fresnel * g_flRimGlow + g_flRimBias ) * step( 0.0001, g_flRimGlow );

		return float4( additive + albedo * rim, alpha );
	}
}
