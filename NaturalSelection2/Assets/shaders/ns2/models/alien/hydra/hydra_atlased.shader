HEADER
{
	Description = "NS2 hydra_atlased";
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

	CreateInputTexture2D( AlbedoMap, Linear, 8, "None", "_albedoMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tAlbedoMap < Channel( RGBA, Box( AlbedoMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( NormalMap, Linear, 8, "None", "_normalMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( SpecularMap, Linear, 8, "None", "_specularMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissiveMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( OpacityMap, Linear, 8, "None", "_opacityMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tOpacityMap < Channel( RGBA, Box( OpacityMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( BlendMap, Linear, 8, "None", "_blendMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tBlendMap < Channel( RGBA, Box( BlendMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( OverlayMap, Linear, 8, "None", "_overlayMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tOverlayMap < Channel( RGBA, Box( OverlayMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flHiddenAmount < Default1( 0.0 ); >;
	float g_flGlowIntensity < Default1( 1.0 ); >;
	float g_flHighlight < Default1( 0.0 ); >;
	float g_flOutline < Default1( 0.0 ); >;
	float g_flTextureIndex < Default1( 0 ); >;
	float g_flNumRows < Default1( 1 ); >;
	float g_flNumColumns < Default1( 2 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		float f = 1 - g_flHiddenAmount;
		float highlightId = g_flHighlight;
		float param = Tex2DS( g_tBlendMap, g_sWrap, i.vTextureCoords.xy ).a;
		float textureSpeed = 2;
		float destroySpeed = 2.5;
		float2 uv = i.vTextureCoords.xy;
		float row = floor( g_flTextureIndex / g_flNumColumns );
		float col = g_flTextureIndex - row * g_flNumColumns;
		uv.x = ( col + uv.x ) / g_flNumColumns;
		uv.y = ( row + uv.y ) / g_flNumRows;
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, i.vTextureCoords.xy );
		sparkNormal = Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy ) );
		sparkSpecular = specularGloss.rgb;
		sparkGloss = specularGloss.a;
		opacity = 1 - pow( abs( param - 1 ), destroySpeed );
		float blend = pow( param, textureSpeed );
		if ( blend + f > 1 )
		{
			sparkAlbedo = Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb;
		}
		else
		{
			sparkAlbedo = Tex2DS( g_tOverlayMap, g_sWrap, i.vTextureCoords.xy * 4 ).rgb;
		}
		opacity = ( opacity + f - 0.5 ) * Tex2DS( g_tOpacityMap, g_sWrap, i.vTextureCoords.xy ).r;
		emission = SampleSrgb( g_tEmissiveMap, g_sWrap, uv ).rgb * f * g_flGlowIntensity;
		clip( opacity - 0.5 );
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
