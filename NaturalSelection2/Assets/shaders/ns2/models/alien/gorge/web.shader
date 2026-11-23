HEADER
{
	Description = "NS2 web";
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

	CreateInputTexture2D( AlbedoMap, Linear, 8, "None", "_albedoMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tAlbedoMap < Channel( RGBA, Box( AlbedoMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flHighlight < Default1( 0.0 ); >;
	float g_flTextureIndex < Default1( 0 ); >;
	float g_flNumRows < Default1( 1 ); >;
	float g_flNumColumns < Default1( 2 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float highlightId = g_flHighlight;
		float2 uv = i.vTextureCoords.xy;
		float row = floor( g_flTextureIndex / g_flNumColumns );
		float col = g_flTextureIndex - row * g_flNumColumns;
		uv.x = ( col + uv.x ) / g_flNumColumns;
		uv.y = ( row + uv.y ) / g_flNumRows;
		float4 tex = SampleSrgb( g_tAlbedoMap, g_sWrap, uv );
		emission = tex.rgb * i.vColor.rgb;
		return float4( emission, opacity );
	}
}
