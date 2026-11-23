HEADER
{
	Description = "NS2 rs_subuv";
}
FEATURES
{
	#include "common/features.hlsl"
}
MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}
COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
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
	#include "ns2/surface_lighting.hlsl"

	CreateInputTexture2D( SubUVMap, Linear, 8, "None", "_subUVMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tSubUVMap < Channel( RGBA, Box( SubUVMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NormalMap, Linear, 8, "None", "_normalMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( SpecularMap, Linear, 8, "None", "_specularMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flEmissiveStrength < Default1( 0.0 ); >;
	float g_flShowDecals < Default1( 0.0 ); >;
	float g_flAnimationspeed < Default1( 1.0 ); >;
	float g_flHorizontalFrames < Default1( 1 ); >;
	float g_flVerticalFrames < Default1( 1 ); >;
	float g_flSmoothBlending < Default1( 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		float timestephard = floor( g_flTime * g_flAnimationspeed );
		float timestepsoft = g_flTime * g_flAnimationspeed;
		float timesteps = lerp( timestephard, timestepsoft, g_flSmoothBlending );
		float2 frames = float2( floor( g_flHorizontalFrames ), floor( g_flVerticalFrames ) );
		float2 framesdiv = float2( 1, 1 ) / frames;
		float substeps = timesteps - frac( timesteps );
		float2 sub1 = float2( fmod( substeps, frames.r ), floor( substeps * framesdiv.r ) );
		float2 sub2 = float2( fmod( 1 + substeps, frames.r ), floor( ( 1 + substeps ) * framesdiv.r ) );
		float2 UV1 = ( i.vTextureCoords.xy + sub1 ) * framesdiv;
		float2 UV2 = ( i.vTextureCoords.xy + sub2 ) * framesdiv;
		float3 SubUVlerp =
			lerp( Tex2DS( g_tSubUVMap, g_sWrap, UV1 ), Tex2DS( g_tSubUVMap, g_sWrap, UV2 ), frac( timesteps ) ).rgb;
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, i.vTextureCoords.xy );
		sparkAlbedo = SubUVlerp;
		emission = SubUVlerp * g_flEmissiveStrength;
		sparkNormal = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy ) );
		sparkSpecular = specularGloss.rgb;
		sparkGloss = specularGloss.a;
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
