HEADER
{
	Description = "NS2 proximity_forcefield_noentry";
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

	CreateInputTexture2D( Glowpoints, Linear, 8, "None", "_glowpoints", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGlowpoints < Channel( RGBA, Box( Glowpoints ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noiseMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( MaskMap, Linear, 8, "None", "_maskMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaskMap < Channel( RGBA, Box( MaskMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NoEntryMap, Linear, 8, "None", "_noEntryMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNoEntryMap < Channel( RGBA, Box( NoEntryMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float distance =
			( SparkViewVector( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs ) /
			  NS2_METRE )
				.z;
		const float max_dist = 6.0f;
		const float min_dist = 2.0f;
		float hide_opacity = saturate( ( max_dist - distance ) / ( max_dist - min_dist ) );
		hide_opacity = hide_opacity * hide_opacity;
		const float3 tint = float3( 16, 38, 55 );
		float3 mask = SampleSrgb( g_tMaskMap, g_sWrap, i.vTextureCoords.xy * 6 + float2( g_flTime * 0, g_flTime * 0.2 ) ).rgb;
		float3 ash = SampleSrgb( g_tGlowpoints, g_sWrap, i.vTextureCoords.xy * 2 ).rgb;
		float3 noise =
			SampleSrgb( g_tNoiseMap, g_sWrap, i.vTextureCoords.xy * .02 + float2( g_flTime * -0.04, g_flTime * -0.1 ) ).rgb;
		float3 noise2 = SampleSrgb( g_tGlowpoints, g_sWrap,
									i.vTextureCoords.xy * float2( 0.02, .5 ) + float2( g_flTime * -0.01, g_flTime * 0.05 ) )
							.rgb;
		float3 noise3 =
			SampleSrgb( g_tGlowpoints, g_sWrap, i.vTextureCoords.xy * float2( 1.2, 1.1 ) + float2( 0, g_flTime * .02 ) ).rgb;
		float fresnel = pow( max( -SparkViewVector( normalize( i.vNormalWs ) ).z, 0 ), 5 );
		float3 worldSpaceNormal = normalize( normalize( i.vNormalWs.yzx ) );
		float mat = worldSpaceNormal.y;
		float2 coords;
		const float warningScale = 1;
		if ( mat >= 0.707 )
		{
			coords = ( ( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz ).yzx / NS2_METRE ).xz * warningScale;
		}
		else if ( mat <= -0.707 )
		{
			coords = float2( -( ( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz ).yzx / NS2_METRE ).x,
							 ( ( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz ).yzx / NS2_METRE ).z ) *
					 warningScale;
		}
		else
		{
			coords = float2( worldSpaceNormal.z *
									 ( ( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz ).yzx / NS2_METRE ).x -
								 worldSpaceNormal.x *
									 ( ( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz ).yzx / NS2_METRE ).z,
							 -( ( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz ).yzx / NS2_METRE ).y ) *
					 warningScale;
		}
		coords += g_flTime * float2( -0.0125, 0.25 );
		float2 warning = SampleSrgb( g_tNoEntryMap, g_sWrap, coords ).rg;
		float circleOpacity = cos( g_flTime );
		circleOpacity = ( circleOpacity * circleOpacity ) * 0.75 + 0.25;
		float4 circle = float4( circleOpacity * warning.r, 0, 0, circleOpacity * warning.r );
		float4 warningText = warning.g;
		float4 composite;
		composite.rgb = warningText.rgb + circle.rgb * ( 1.0 - warningText.a );
		composite.a = circle.a + ( 1.0 - circle.a ) * warningText.a;
		float3 forcefield = ( noise * ash * noise3 * 10 * noise2 + ( 0.0008 * mask ) ) * tint;
		float3 finalComp = composite.rgb + ( 1.0 - composite.a ) * forcefield;
		emission = finalComp * hide_opacity;
		return float4( emission, opacity );
	}
}
