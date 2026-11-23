HEADER
{
	Description = "NS2 mixedexostatus";
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

	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissiveMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( AlternateMap, Linear, 8, "None", "_alternateMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tAlternateMap < Channel( RGBA, Box( AlternateMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flBaseIsArm < Default1( 0 ); >;
	float g_flAlternateIsArm < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float alternate = step( 1.5, i.vTextureCoords.xy.x );
		float2 uv = i.vTextureCoords.xy - float2( 2.0 * alternate, 0.0 );
		float4 tex = lerp( SampleSrgb( g_tEmissiveMap, g_sWrap, uv ), SampleSrgb( g_tAlternateMap, g_sWrap, uv ), alternate );
		float arm = lerp( g_flBaseIsArm, g_flAlternateIsArm, alternate );
		emission = tex.rgb * lerp( float3( 1, 1, 1 ), i.vColor.rgb, arm );
		opacity = lerp( 1.0, tex.a * i.vColor.a, arm );
		return float4( emission, opacity );
	}
}
