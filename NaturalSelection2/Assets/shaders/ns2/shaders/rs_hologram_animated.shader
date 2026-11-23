HEADER
{
	Description = "NS2 rs_hologram_animated";
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
	CreateInputTexture2D( FlickerMap, Linear, 8, "None", "_flickerMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tFlickerMap < Channel( RGBA, Box( FlickerMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flEmissiveStrength < Default1( 0.0 ); >;
	float g_flEmissiveMod < Default1( 1.0 ); >;
	float g_flDistortionIntensity < Default1( 0.01 ); >;
	float g_flScanlineIntensity < Default1( 0.8 ); >;
	float g_flShowDecals < Default1( 0.0 ); >;
	float g_flAnimationspeed < Default1( 1.0 ); >;
	float g_flHorizontalFrames < Default1( 1 ); >;
	float g_flVerticalFrames < Default1( 1 ); >;
	float g_flSmoothBlending < Default1( 1 ); >;
	CreateInputTexture2D( Noisetex, Linear, 8, "None", "_noisetex", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoisetex < Channel( RGBA, Box( Noisetex ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( Lcdtex, Linear, 8, "None", "_lcdtex", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tLcdtex < Channel( RGBA, Box( Lcdtex ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float distanceblend( float range, float offset, float3 vsPos )
	{
		return saturate( ( length( vsPos ) - offset ) / range );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float ts = g_flAnimationspeed;
		const float noisemult = 0.65;
		const float ScanlineScale = 1.0 / 64;
		float Scanlines =
			saturate( abs( frac( i.vTextureCoords.xy.y / ScanlineScale + ( 1 - g_flTime ) ) - 0.5 ) + g_flScanlineIntensity );
		float2 psjitter = floor( g_flTime * ts ) / ts + ( g_flTime * 0.024 + float2( -0.005, -0.3 ) );
		float3 uvjitter =
			Tex2DS( g_tNoisetex, g_sWrap, i.vTextureCoords.xy * 2 + Tex2DS( g_tNoisetex, g_sWrap, psjitter ).rg ).rgb;
		float2 distortionUV = float2( Tex2DS( g_tNoisetex, g_sWrap, float2( i.vTextureCoords.xy.g, psjitter.r ) ).b, 0 ) *
							  uvjitter.b * g_flDistortionIntensity;
		float timestephard = floor( g_flTime * g_flAnimationspeed );
		float timestepsoft = g_flTime * g_flAnimationspeed;
		float timesteps = lerp( timestephard, timestepsoft, g_flSmoothBlending );
		float2 frames = float2( floor( g_flHorizontalFrames ), floor( g_flVerticalFrames ) );
		float2 framesdiv = float2( 1, 1 ) / frames;
		float substeps = timesteps - frac( timesteps );
		float2 sub1 = float2( fmod( substeps, frames.r ), floor( substeps * framesdiv.r ) );
		float2 sub2 = float2( fmod( 1 + substeps, frames.r ), floor( ( 1 + substeps ) * framesdiv.r ) );
		float2 UV1 = ( i.vTextureCoords.xy + sub1 ) * framesdiv - abs( distortionUV );
		float2 UV2 = ( i.vTextureCoords.xy + sub2 ) * framesdiv - abs( distortionUV );
		float3 SubUVlerp =
			lerp( Tex2DS( g_tEmissiveMap, g_sWrap, UV1 ).rgb, Tex2DS( g_tEmissiveMap, g_sWrap, UV2 ).rgb, frac( timesteps ) ) *
			saturate( uvjitter.r + noisemult );
		float3 lcdpattern = Tex2DS( g_tLcdtex, g_sWrap, i.vTextureCoords.xy * 512 ).rgb * SubUVlerp * 3;
		float3 screenblend =
			lerp( lcdpattern, SubUVlerp,
				  distanceblend( 2.0, 2.0,
								 ( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz -
													g_vCameraPositionWs ) /
								   NS2_METRE ) ) );
		float3 scanlineblend =
			lerp( screenblend * Scanlines, screenblend,
				  distanceblend( 4.0, 10.0,
								 ( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz -
													g_vCameraPositionWs ) /
								   NS2_METRE ) ) );
		emission = scanlineblend * g_flEmissiveStrength * g_flEmissiveMod;
		return float4( emission, opacity );
	}
}
