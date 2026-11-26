HEADER
{
	Description = "Source 1 additive beam";
}

FEATURES
{
	Feature( F_SOURCE_IGNORE_Z, 0..1, "Source 1" );
}

MODES
{
	Forward();
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	float3 Position : POSITION < Semantic( None ); >;
	float4 Color : COLOR0 < Semantic( None ); >;
	float2 UV : TEXCOORD0 < Semantic( None ); >;
};

struct PixelInput
{
	float4 Position : SV_Position;
	float2 UV : TEXCOORD0;
	float4 Color : COLOR0;
};

VS
{
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i;
		i.Position = Position3WsToPs( v.Position );
		i.UV = v.UV;
		i.Color = v.Color;
		return i;
	}
}

PS
{
	StaticCombo( S_SOURCE_IGNORE_Z, F_SOURCE_IGNORE_Z, Sys( ALL ) );
	CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Material", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tColor < Channel( RGBA, Box( TextureColor ), Srgb ); SrgbRead( false ); OutputFormat( BC7 ); >;
	bool g_bSourceSrgb < Default( 1 ); >;
	int SamplerIndex < Attribute( "SamplerIndex" ); >;

	RenderState( CullMode, NONE );
	RenderState( DepthEnable, !S_SOURCE_IGNORE_Z );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, ONE );

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 color = g_tColor.Sample( Bindless::GetSampler( SamplerIndex ), i.UV );
		if ( g_bSourceSrgb ) color.rgb = SrgbGammaToLinear( color.rgb );
		return color * i.Color;
	}
}
