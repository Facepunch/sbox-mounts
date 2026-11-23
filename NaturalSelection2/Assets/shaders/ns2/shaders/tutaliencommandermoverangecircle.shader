HEADER
{
	Description = "NS2 tutaliencommandermoverangecircle";
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
	float3 vSparkPosition : TEXCOORD14;
};
VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		i.vSparkPosition = v.vPositionOs.yzx / 40.0;
		return FinalizeVertex( i );
	}
}
PS
{
	#include "ns2/emissive_pass.hlsl"
	RenderState( DstBlend, ONE );

	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissiveMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flStartTime < Default1( 0 ); >;
	float g_flFadeInTime < Default1( 0 ); >;
	float g_flFadeOutTime < Default1( 0 ); >;
	float g_flFadeOutInfluence < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		const float pi = 3.14159265359f;
		const float rotationSpeed = 0.25f;
		const float pulseFreq = 1.0f;
		const float pulseMaxStrength = 2.5f;
		const float pulseMinStrength = 0.5f;
		const float fadeInDuration = 1.0f;
		const float fadeOutDuration = 1.0f;
		const float heightFadeWidth = 2.0f;
		float time2 = g_flTime - g_flStartTime;
		float st = 0.0f;
		float ct = 0.0f;
		float theta = time2 * rotationSpeed;
		sincos( theta, st, ct );
		float2 uv = float2( ct * ( i.vTextureCoords.xy.x - 0.5f ) - st * ( i.vTextureCoords.xy.y - 0.5f ) + 0.5f,
							st * ( i.vTextureCoords.xy.x - 0.5f ) + ct * ( i.vTextureCoords.xy.y - 0.5f ) + 0.5f );
		float cropX = max( floor( -abs( uv.x - 0.5f ) + 1.5 ), 0 );
		float cropY = max( floor( -abs( uv.y - 0.5f ) + 1.5 ), 0 );
		float crop = cropX * cropY;
		float pulseMult =
			( -cos( time2 * pi * 2 * pulseFreq ) * 0.5f + 0.5f ) * ( pulseMaxStrength - pulseMinStrength ) + pulseMinStrength;
		float fadeIn = ( saturate( ( g_flTime - g_flFadeInTime ) / fadeInDuration ) );
		fadeIn = fadeIn * fadeIn;
		float fadeOut = ( g_flFadeOutInfluence * ( saturate( ( g_flFadeOutTime - g_flTime ) / fadeOutDuration ) ) ) -
						g_flFadeOutInfluence + 1;
		fadeOut = ( 2 * fadeOut ) - ( fadeOut * fadeOut );
		float fade = fadeIn * fadeOut;
		float hFade = saturate( 1.0f - ( 2.0f * ( abs( i.vSparkPosition.y ) / heightFadeWidth ) ) );
		hFade = hFade * hFade;
		emission = Tex2DS( g_tEmissiveMap, g_sWrap, uv ).rgb * pulseMult * crop * fade * hFade;
		return float4( emission, opacity );
	}
}
