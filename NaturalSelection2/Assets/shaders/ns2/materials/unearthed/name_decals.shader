//=============================================================================
// Port of NS2 ns2/materials/unearthed/name_decals.surface_shader
//
//     float3 albedo = tex2D(albedoMap, input.texCoord);
//     float shininess  = (albedo.b + .2) * .3;
//     float glossiness = 20;
//     material.albedo   = albedo.r * decalColor;
//     material.tsNormal = DecodeNormal( tex2D(normalMap, input.texCoord) );
//     material.specular = albedo.g * shininess;
//     material.gloss    = glossiness / 255.0;
//     material.opacity  = albedo.g;
//
// A mask-packed decal: one greyscale texture drives everything. R is the ink
// coverage that gets tinted by decalColor, G is both the coverage mask and the
// specular amount, B modulates shininess. NS2 assigns a float4 to a float3
// (material.albedo = albedo.r * decalColor) which HLSL truncates to .rgb, so
// decalColor's alpha never contributes.
//
// <param name="alphaTest">false</param> is explicit here, so unlike the other
// decal shaders this is a genuine alpha blend rather than a binary cutout.
//=============================================================================

HEADER
{
	Description = "NS2 Name Decals";
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
	#define S_TRANSLUCENT 1
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
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		return FinalizeVertex( i );
	}
}

PS
{
	// Depth and blend state are set explicitly below. sbox_pixel.fxc installs its
	// own translucent depth/blend state unless told not to, and a duplicate
	// RenderState does not override the earlier one - it compiles to a different
	// state entirely - so the guards are required, not just tidiness.
	//
	// S_TRANSLUCENT is also what emits BoolAttribute( translucent, true ), which
	// is what puts the surface in the translucent pass. Without it the draw
	// happens in the opaque pass, where the framebuffer copy and depth chain have
	// not been resolved yet, and sampling them just smears the colour buffer.
	#define DEPTH_STATE_ALREADY_SET 1
	#define BLEND_MODE_ALREADY_SET 1
	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 1
	#endif
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
	RenderState( BlendOp, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap, Linear, 8, "None",             "_color",  ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap, Linear, 8, "NormalizeNormals", "_normal", ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );

	Texture2D g_tAlbedoMap < Channel( RGBA, Box( AlbedoMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 g_vDecalColor < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 1.0, 1.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, i.vTextureCoords.xy ).rgb;
		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, i.vTextureCoords.xy );

		float shininess = ( albedo.b + 0.2 ) * 0.3;

		Ns2ApplySpecularGloss( m, albedo.r * g_vDecalColor.rgb, saturate( albedo.g * shininess ), 20.0 / 255.0 );

		m.Opacity = albedo.g;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = TransformNormal( Ns2DecodeNormal( normal ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = i.vTextureCoords.xy;

		return ShadingModelStandard::Shade( i, m );
	}
}
