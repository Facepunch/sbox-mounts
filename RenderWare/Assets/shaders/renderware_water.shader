
HEADER
{
	Description = "Renderware water: GTA's water_old texture, scrolling, tinted with the timecyc water colour, unlit and translucent";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward(); // translucent - no depth pass, no shadows
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
	// alpha blended in the translucent pass, no depth writes - common/pixel.hlsl (sbox_pixel.fxc) sets that up
	#define S_TRANSLUCENT 1
	#include "common/pixel.hlsl"

	RenderState( CullMode, NONE );

	SamplerState g_sWrap < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

	// gamma space, set by WorldLoader from the time cycle
	float3 g_vWaterColor < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Water" ); >;

	// texture tiles per 32 GTA units (baked into the uvs), scrolled this many tiles per second
	float2 g_vScroll < Default2( 0.03, 0.02 ); UiGroup( "Water" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy + g_vScroll * g_flTime;
		float4 tex = g_tColor.Sample( g_sWrap, uv );

		float4 color = float4( tex.rgb * pow( g_vWaterColor, 2.2 ), tex.a );

		float3 positionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		color.rgb = Fog::Apply( positionWs, i.vPositionSs.xy, color.rgb );

		return color;
	}
}
