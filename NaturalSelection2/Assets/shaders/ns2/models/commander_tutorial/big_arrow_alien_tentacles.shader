HEADER
{
	Description = "NS2 big_arrow_alien_tentacles";
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
	#include "ns2/surface_lighting.hlsl"

	RenderState( CullMode, NONE );

	CreateInputTexture2D( AlbedoMap, Linear, 8, "None", "_albedoMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tAlbedoMap < Channel( RGBA, Box( AlbedoMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NormalMap, Linear, 8, "None", "_normalMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( SpecularMap, Linear, 8, "None", "_specularMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissiveMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flFade < Default1( 1 ); >;
	CreateInputTexture2D( SmokeMap, Linear, 8, "None", "_smokeMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tSmokeMap < Channel( RGBA, Box( SmokeMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		const float pi = 3.14159265359;
		const float3 offset = float3( 0, -.068, -0.3 );
		const float emissiveBalance = 0.5;
		const float crushWhite = 157.0 / 255.0;
		const float crushBlack = 29.0 / 255.0;
		float dist = length( i.vSparkPosition + offset );
		float time2 = g_flTime - dist * 2.0 + 0.25;
		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy ).rgb;
		sparkAlbedo = albedo;
		sparkNormal = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy ) );
		float4 specGloss = Tex2DS( g_tSpecularMap, g_sWrap, i.vTextureCoords.xy );
		sparkSpecular = specGloss.rgb;
		sparkGloss = specGloss.a;
		if ( g_flFade < 1 )
		{
			float samp = Tex2DS( g_tSmokeMap, g_sWrap, i.vTextureCoords.xy ).r;
			samp = saturate( ( samp - crushBlack ) / ( crushWhite - crushBlack ) );
			opacity = saturate( ( g_flFade * 2 - 1 ) + samp );
		}
		float3 emissive = Tex2DS( g_tEmissiveMap, g_sWrap, i.vTextureCoords.xy ).rgb;
		float pulse = sin( time2 * pi ) * 0.4 + 0.6;
		emission = emissive * pulse * opacity;
		clip( opacity - 0.5 );
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
