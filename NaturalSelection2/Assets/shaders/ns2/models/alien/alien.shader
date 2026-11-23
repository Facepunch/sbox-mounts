//=============================================================================
// Port of NS2 ns2/models/alien/alien.surface_shader
//
//     <param name="alphaTest">true</param>
//
//     float2 uv = input.texCoord;
//     float row = floor(textureIndex / numColumns);
//     float col = textureIndex - row * numColumns;
//     uv.x = (col + uv.x) / numColumns;
//     uv.y = (row + uv.y) / numRows;
//
//     float f            = 1 - hiddenAmount;
//     float param        = tex2D(blendMap, uv).a;
//     float textureSpeed = 2;
//     float destroySpeed = 2.5;
//
//     float4 specularGloss = tex2D(specularMap, uv);
//     material.tsNormal  = DecodeNormal( tex2D(normalMap, uv) );
//     material.specular  = specularGloss.rgb;
//     material.gloss     = specularGloss.a;
//
//     float opacity = paramSlow(param, destroySpeed);   // 1 - pow(abs(x),p)
//     float blend   = paramQuick(param, textureSpeed);  // pow(x,p)
//
//     if (blend + f > 1) material.albedo = tex2D(albedoMap, uv).rgb;
//     else               material.albedo = tex2D(overlayMap, uv * 4).rgb;
//
//     material.opacity  = opacity + f - 0.5;
//     material.emissive = tex2D(emissiveMap, uv).rgb * f * glowIntensity;
//
//     material.id      = 1 * highlight;
//     material.outline = outline;
//
// hiddenAmount / highlight / outline / textureIndex are set by gameplay code,
// never by a .material. numRows / numColumns are material driven (sprite atlas
// flipbooks) and default to 1. With hiddenAmount = 0 the albedoMap branch
// always wins and the alpha test always passes; blendMap defaults to white so
// param = 1 reproduces that exactly.
//
// material.id (alien vision highlight) and material.outline have no s&box
// equivalent; they are exposed as uniforms but are visually inert.
//=============================================================================

HEADER
{
	Description = "NS2 Alien";
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

	CreateInputTexture2D( AlbedoMap,   Linear, 8, "None",             "_color",     ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",    ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap, Linear, 8, "None",             "_spec",      ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None",             "_selfillum", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( BlendMap,    Linear, 8, "None",             "_blend",     ",0/,0/0", Default4( 1.0, 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( OverlayMap,  Linear, 8, "None",             "_overlay",   ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tBlendMap    < Channel( RGBA, Box( BlendMap ),    Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tOverlayMap  < Channel( RGBA, Box( OverlayMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flHiddenAmount < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flGlowIntensity< UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;
	float g_flHighlight    < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flTextureIndex < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 64 ); >;
	float g_flOutline      < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flNumRows      < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 1, 16 ); >;
	float g_flNumColumns   < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 1, 16 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;
		float row = floor( g_flTextureIndex / g_flNumColumns );
		float col = g_flTextureIndex - row * g_flNumColumns;
		uv.x = ( col + uv.x ) / g_flNumColumns;
		uv.y = ( row + uv.y ) / g_flNumRows;

		float f = 1.0 - g_flHiddenAmount;
		float param = Tex2DS( g_tBlendMap, g_sWrap, uv ).a;

		float opacity = 1.0 - pow( abs( param ), 2.5 );
		float blend = param * param;

		m.Opacity = opacity + f - 0.5;
		clip( m.Opacity - 0.5 );
		m.Opacity = 1.0;

		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );

		float3 albedo = blend + f > 1.0
			? Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb
			: Tex2DS( g_tOverlayMap, g_sWrap, uv * 4.0 ).rgb;

		Ns2ApplySpecularGloss( m, albedo * i.vTintColor.rgb, specularGloss.rgb, specularGloss.a );

		m.Emission = Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, uv ).rgb ) * f * g_flGlowIntensity;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = TransformNormal( Ns2DecodeNormal( normal ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
