//=============================================================================
// Port of NS2 ns2/shaders/ShotgunLights_emissive.surface_shader
//
//     float mapId = max(textureIndex - 1.0f, 0.0f);
//     float row = floor(mapId / numColumns);
//     float col = mapId - row*numColumns;
//     float2 uvMult = float2(1.0f/numColumns, 1.0f/numRows);
//     float2 uvOffset = float2(col, row);
//
//     float lightIdx = 1 + floor( input.texCoord.x * 3.0 ) + 3.0 * floor( input.texCoord.y * 2 );
//     float2 texCoord = input.texCoord;
//     texCoord.y = texCoord.y * 0.5;
//
//     if ( lightIdx > ammo )
//     {
//         texCoord.y = texCoord.y + 0.5;
//         texCoord += uvOffset;  texCoord *= uvMult;
//         material.albedo   = tex2D(albedoMap, texCoord).rgb;
//         material.emissive = float4(0,0,0,0);
//     }
//     else
//     {
//         texCoord += uvOffset;  texCoord *= uvMult;
//         material.albedo   = tex2D(albedoMap, texCoord).rgb;
//         material.emissive = tex2D(emissiveMap, texCoord).rgb;
//     }
//     material.id = 1*highlight;    // for alien vision
//
// The shotgun's shell counter. The UV space is a 3x2 grid of six lamps; a lamp
// whose index exceeds the current ammo count samples the lower ("off") half of
// the strip and emits nothing. That whole strip is then placed inside a skin
// atlas of numColumns x numRows via textureIndex, offset by one because the
// first two shotgun skins predate the lights.
//
// normalMap and specularMap are declared but their uses are commented out in
// the NS2 source, so the surface writes no normal or specular at all. Kept as
// inputs so materials still bind; the port writes the geometric normal and
// zero specular to match.
//
// material.id feeds Spark's alien vision pass, which has no s&box equivalent.
//=============================================================================

HEADER
{
	Description = "NS2 Shotgun Lights Emissive";
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
	#define S_ALPHA_TEST 0
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
	float4 vColor : COLOR0;
	float4 vTintColor : COLOR1;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;

		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;

		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap,   Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap, Linear, 8, "None",             "_spec",     ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flHighlight    < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flAmmo         < UiGroup( ",0/,0/0" ); Default1( 6.0 ); Range1( 0, 6 ); >;
	float g_flTextureIndex < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 16 ); >;
	float g_flNumRows      < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 1, 16 ); >;
	float g_flNumColumns   < UiGroup( ",0/,0/0" ); Default1( 3.0 ); Range1( 1, 16 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		float mapId = max( g_flTextureIndex - 1.0, 0.0 );
		float row = floor( mapId / g_flNumColumns );
		float col = mapId - row * g_flNumColumns;
		float2 uvMult = float2( 1.0 / g_flNumColumns, 1.0 / g_flNumRows );
		float2 uvOffset = float2( col, row );

		float lightIdx = 1.0 + floor( uv.x * 3.0 ) + 3.0 * floor( uv.y * 2.0 );

		float2 texCoord = uv;
		texCoord.y *= 0.5;

		bool lightOff = lightIdx > g_flAmmo;
		if ( lightOff )
			texCoord.y += 0.5;

		texCoord = ( texCoord + uvOffset ) * uvMult;

		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, texCoord ).rgb;
		float3 emissive = lightOff ? 0.0 : Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, texCoord ).rgb );

		Ns2ApplySpecularGloss( m, albedo * i.vTintColor.rgb, 0.0, 0.0 );
		m.Emission = emissive;

		m.Opacity = 1.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = i.vNormalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
