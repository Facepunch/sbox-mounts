HEADER
{
	Description = "Halo 2 lens reflection";
}
MODES
{
	Forward();
}
COMMON
{
	#include "system.fxc"
}
struct VertexInput
{
	float2 position : POSITION < Semantic( position ); >;
	float2 uv : TEXCOORD0 < Semantic( texcoord ); >;
};
struct PixelInput
{
	float2 uv : TEXCOORD0;
	float4 position : SV_Position;
};
VS
{
	float4 Viewport < Source( Viewport ); >;
	float4 LensRotation < Attribute( "LensRotation" ); Default4( 0,0,1,0 ); >;

	PixelInput MainVs( VertexInput i )
	{
		float2 delta = i.position - LensRotation.xy;
		float2 rotated = LensRotation.z * delta + LensRotation.w * float2( -delta.y, delta.x );
		float2 pixel = trunc( (LensRotation.xy + rotated) * 16.0 ) / 16.0;
		float2 clip = 2.0 * (pixel - Viewport.xy) / Viewport.zw - 1.0;
		PixelInput o;
		o.position = float4( clip.x, -clip.y, 0, 1 );
		o.uv = i.uv;
		return o;
	}
}
PS
{
	RenderState( DepthEnable, false );
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, NONE );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, ONE );
	Texture2D LensBitmap < Attribute( "LensBitmap" ); SrgbRead( false ); >;
	SamplerState LensSampler < Filter( MIN_MAG_MIP_LINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	float4 LensTint < Attribute( "LensTint" ); Default4( 1,1,1,1 ); >;
	float LensSecondaryAlpha < Attribute( "LensSecondaryAlpha" ); Default( 0 ); >;
	StructuredBuffer<float4> LensVisibility < Attribute( "LensVisibility" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 bitmap = LensBitmap.Sample( LensSampler, i.uv ).rgb;
		float3 squared = bitmap * bitmap;
		float3 highlights = squared * squared * squared;
		return float4( bitmap * LensTint.rgb + highlights * (1.0 - LensTint.rgb) * LensSecondaryAlpha, LensTint.a * LensVisibility[0].x );
	}
}
