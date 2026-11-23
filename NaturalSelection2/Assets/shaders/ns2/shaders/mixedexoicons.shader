HEADER
{
	Description = "NS2 mixedexoicons";
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
	float g_flHiddenAmount < Default1( 0.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float alternate = step( 1.5, i.vTextureCoords.xy.x );
		float2 uv = i.vTextureCoords.xy - float2( 2.0 * alternate, 0.0 );
		float4 tex = lerp( SampleSrgb( g_tEmissiveMap, g_sWrap, uv ), SampleSrgb( g_tAlternateMap, g_sWrap, uv ), alternate );
		emission = tex.rgb * i.vColor.rgb;
		opacity = tex.a * i.vColor.a * ( 1.0 - g_flHiddenAmount );
		return float4( emission, opacity );
	}
}
