//=============================================================================
// Port of NS2 core/shaders/Terrain.surface_shader
//
//     float2 texCoords = input.wsPosition.zx * 0.307578125; //39.37 / 128
//     float4 albedo = tex2D(albedoMap, texCoords);
//
//     float shininess  = albedo.a * 20;
//     float glossiness = 20;
//
//     material.albedo   = albedo.rgb;
//     material.tsNormal = DecodeNormal( tex2D(normalMap, texCoords) );
//     material.specular = albedo.rgb * shininess;
//     material.gloss    = glossiness / 255.0;
//     material.id       = decals;
//
// The mesh UVs are ignored entirely: terrain is projected from world space so
// tiles never stretch across slopes and neighbouring chunks line up for free.
//
// The projection scale is worth unpacking. Spark authors in metres and its
// comment gives the constant away as 39.37 / 128, so the texture repeats every
// 128 INCHES, not every 128 metres. s&box already works in inches, which means
// the metre conversion cancels out completely and the whole thing collapses to
// a plain divide by 128.
//
// Spark is Y-up so its horizontal plane is XZ, and it samples .zx. s&box is
// Z-up, so the same pair of axes is simply .xy - and the swap in Spark's .zx
// happens to be exactly the axis remap the map loader already applies, so the
// texture lands on the terrain the same way round.
//
// specular = albedo * albedo.a * 20 makes the specular colour track the diffuse
// and lets the artist paint shininess into the albedo alpha. gloss is a
// constant 20/255, a very rough surface.
//=============================================================================

HEADER
{
	Description = "NS2 Terrain";
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
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		return FinalizeVertex( ProcessVertex( v ) );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap, Linear, 8, "None",             "_color",  ",0/,0/0", Default4( 1.0, 1.0, 1.0, 0.0 ) );
	CreateInputTexture2D( NormalMap, Linear, 8, "NormalizeNormals", "_normal", ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );

	Texture2D g_tAlbedoMap < Channel( RGBA, Box( AlbedoMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flDecals < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		// Restore absolute world coordinates before projecting terrain UVs.
		float3 vPositionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		float2 uv = vPositionWs.xy * ( 0.307578125 / NS2_METRE );

		float4 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, uv );
		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );

		Ns2ApplySpecularGloss( m, albedo.rgb, albedo.rgb * ( albedo.a * 20.0 ), 20.0 / 255.0 );

		m.Opacity = 1.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = TransformNormal( Ns2DecodeNormal( normal ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
