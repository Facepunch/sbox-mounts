//=============================================================================
// Port of NS2 core/shaders/Decal.surface_shader
//
//     intensity = endTime != 0 ? clamp((endTime - time)/1, 0, 1) : 1;
//     if (scale != 0) {
//         smoothEdge = abs(osPosition.y / scale) * 0.5;
//         texCoord.x = clamp(texCoord.x + smoothEdge, -1, 1);
//         texCoord.y = clamp(texCoord.y - smoothEdge, -1, 1);
//         intensity *= clamp(1 - (length(osPosition) / (scale*0.5))
//                      * (abs(osPosition.y) / (scale*0.5)), 0, 1);
//     }
//     material.specular = specularGloss.rgb * intensity;
//     material.gloss    = specularGloss.a   * intensity;
//     material.opacity  = tex2D(opacityMap, texCoord).r * intensity;
//=============================================================================

HEADER
{
	Description = "NS2 Decal";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#ifndef S_ALPHA_TEST
	#define S_ALPHA_TEST 1
	#endif

	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 0
	#endif

	#include "common/shared.hlsl"

	#define CUSTOM_MATERIAL_INPUTS
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float3 vPositionOs : TEXCOORD14;
	float4 vColor : COLOR0;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );

		i.vPositionOs = v.vPositionOs.xyz;
		i.vColor = v.vColor;

		return FinalizeVertex( i );
	}
}

PS
{
	#include "ns2/alpha_test.hlsl"
	#include "ns2/ns2.hlsl"

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap,   Linear, 8, "None",             "_color",   ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",  ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap, Linear, 8, "None",             "_spec",    ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( OpacityMap,  Linear, 8, "None",             "_opacity", ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tOpacityMap  < Channel( RGBA, Box( OpacityMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flEndTime < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1000 ); >;
	float g_flScale   < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1000 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float intensity = 1.0;

		if ( g_flEndTime != 0.0 )
			intensity = clamp( g_flEndTime - g_flTime, 0.0, 1.0 );

		float2 texCoord = i.vTextureCoords.xy;

		if ( g_flScale != 0.0 )
		{
			float smoothEdge = abs( i.vPositionOs.y / g_flScale ) * 0.5;
			texCoord.x = clamp( texCoord.x + smoothEdge, -1.0, 1.0 );
			texCoord.y = clamp( texCoord.y - smoothEdge, -1.0, 1.0 );

			float distance = length( i.vPositionOs );
			intensity *= clamp( 1.0 - ( distance / ( g_flScale * 0.5 ) )
				* ( abs( i.vPositionOs.y ) / ( g_flScale * 0.5 ) ), 0.0, 1.0 );
		}

		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, texCoord ).rgb;
		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, texCoord );
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, texCoord );

		Ns2ApplySpecularGloss( m, albedo, specularGloss.rgb * intensity, specularGloss.a * intensity );

		m.Opacity = Tex2DS( g_tOpacityMap, g_sWrap, texCoord ).r * intensity;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = TransformNormal( Ns2DecodeNormal( normal ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = texCoord;

		return Ns2ShadeAlphaTest( i, m, TextureDimensions2DS( g_tOpacityMap, 0 ), g_flAlphaTestReference );
	}
}
