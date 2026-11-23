HEADER
{
	Description = "NS2 animated_model";
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

	CreateInputTexture2D( AlbedoMap, Linear, 8, "None", "_albedoMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tAlbedoMap < Channel( RGBA, Box( AlbedoMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flNumFrames < Default1( 1 ); >;
	float g_flFramesPerSecond < Default1( 30 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float frame = floor( g_flFramesPerSecond * g_flTime );
		float vOffset = ( 1.0f / g_flNumFrames ) * frame;
		float v = i.vTextureCoords.xy.y / g_flNumFrames;
		float2 uvs = float2( i.vTextureCoords.xy.x, v ) + float2( 0, vOffset );
		float4 tex = Tex2DS( g_tAlbedoMap, g_sWrap, uvs );
		emission = tex.rgb / tex.a;
		opacity = tex.a;
		return float4( emission, opacity );
	}
}
