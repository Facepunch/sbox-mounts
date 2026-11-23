HEADER
{
	Description = "NS2 dropship_fx_thrusters";
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
	float4 vColor : COLOR0;
};
VS
{
	#include "ns2/surface_vertex.hlsl"
}
PS
{
	#include "ns2/emissive_pass.hlsl"
	RenderState( DstBlend, ONE );
	RenderState( CullMode, NONE );

	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissiveMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( MaskMap, Linear, 8, "None", "_maskMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaskMap < Channel( RGBA, Box( MaskMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float3 tint = float3( 15.1, 18.3, 38.5 );
		float4 texmask = SampleSrgb( g_tMaskMap, g_sWrap, i.vTextureCoords.xy );
		float4 tex = SampleSrgb( g_tEmissiveMap, g_sWrap,
								 i.vTextureCoords.xy * float2( 1.95, 1.67 ) + float2( g_flTime * .03, g_flTime * -8 ) );
		float4 tex2 = SampleSrgb( g_tEmissiveMap, g_sWrap,
								  i.vTextureCoords.xy * float2( 1.7, 1.8 ) + float2( .5 + g_flTime * -.02, g_flTime * -9 ) );
		float4 tex3 = SampleSrgb( g_tEmissiveMap, g_sWrap,
								  i.vTextureCoords.xy * float2( 1.2, 1.5 ) + float2( g_flTime * -.01, g_flTime * -7 ) );
		emission = ( tex.rgb * tex2.rgb ) * tint * texmask.rgb;
		return float4( emission, opacity );
	}
}
