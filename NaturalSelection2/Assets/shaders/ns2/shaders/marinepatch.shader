//=============================================================================
// Port of NS2 core/shaders/Marinepatch.surface_shader
//
//     if( patchIndex >= 0 )
//     {
//         float f   = 1 - hiddenAmount;
//         float2 uv = input.texCoord;
//         float row = floor(patchIndex / numColumns);
//         float col = patchIndex - row * numColumns;
//         uv.x = (col+uv.x) / numColumns;
//         uv.y = (row+uv.y) / numRows;
//
//         material.albedo   = tex2D(atlas, uv).rgb;
//         material.opacity  = tex2D(opacityMap, uv).r * f;
//         material.emissive = tex2D(emissiveMap, uv).rgb;
//     }
//     else
//     {
//         material.albedo  = float3(1,0,0);
//         material.opacity = 0;
//     }
//     material.id = 1 * highlight;    // for alien vision
//
// The unlockable shoulder patch on a marine model. One atlas holds every patch
// design in a grid and patchIndex picks a cell, row-major from the top left.
// A negative index means "no patch": the branch writes opaque red but zero
// opacity, so alphaTest clips it away entirely - the red never shows.
//
// hiddenAmount is a 0..1 fade the game drives to hide the patch, applied to
// opacity only, so with alphaTest it pops out rather than fading.
//
// The Halloween 2018 two-frame animation is commented out in the shipped
// original, so patchIndex maps straight to a cell. Left out.
//
// material.id feeds alien vision, which the mount does not render. Dropped.
//
// This is player gear rather than map geometry, so it will only ever be seen
// if an addon spawns a marine model directly.
//=============================================================================

HEADER
{
	Description = "NS2 Marine Patch";
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
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vTintColor : COLOR1;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );

		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;

		return FinalizeVertex( i );
	}
}

PS
{
	#include "ns2/alpha_test.hlsl"
	#include "ns2/ns2.hlsl"

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( Atlas,       Linear, 8, "None", "_color",    ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( OpacityMap,  Linear, 8, "None", "_opacity",  ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( EmissiveMap, Srgb,   8, "None", "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tAtlas       < Channel( RGBA, Box( Atlas ),       Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tOpacityMap  < Channel( RGBA, Box( OpacityMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;

	float g_flNumRows < UiGroup( ",0/,0/0" ); Default1( 4.0 ); Range1( 1, 64 ); >;
	float g_flNumColumns < UiGroup( ",0/,0/0" ); Default1( 4.0 ); Range1( 1, 64 ); >;
	float g_flPatchIndex < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( -1, 256 ); >;
	float g_flHiddenAmount < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flHighlight < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		// A negative index means no patch: opaque red, zero opacity, clipped away.
		if ( g_flPatchIndex < 0.0 )
		{
			clip( -1.0 );
			discard;
		}

		float row = floor( g_flPatchIndex / g_flNumColumns );
		float col = g_flPatchIndex - row * g_flNumColumns;

		float2 cellUv = float2( ( col + uv.x ) / g_flNumColumns, ( row + uv.y ) / g_flNumRows );

		float3 albedo = Tex2DS( g_tAtlas, g_sWrap, cellUv ).rgb;

		// The original writes no specular or gloss, so Spark leaves both at zero.
		Ns2ApplySpecularGloss( m, albedo * i.vTintColor.rgb, float3( 0.0, 0.0, 0.0 ), 0.0 );

		m.Emission = Tex2DS( g_tEmissiveMap, g_sWrap, cellUv ).rgb;
		m.Opacity = Tex2DS( g_tOpacityMap, g_sWrap, cellUv ).r * ( 1.0 - g_flHiddenAmount );
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = i.vNormalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = cellUv;

		return Ns2ShadeAlphaTest( i, m, TextureDimensions2DS( g_tOpacityMap, 0 ), g_flAlphaTestReference );
	}
}
