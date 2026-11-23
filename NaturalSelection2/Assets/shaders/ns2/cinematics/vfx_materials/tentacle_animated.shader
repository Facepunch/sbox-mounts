//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/tentacle_animated.surface_shader
//
//     <param name="twoSided">true</param>
//     <param name="alphaTest">true</param>
//
//     float frame   = floor(framesPerSecond * time);
//     float vOffset = (1.0f / numFrames) * frame;
//     float v       = input.texCoord.y / numFrames;
//     float2 uvs    = float2(input.texCoord.x, v) + float2(0, vOffset);
//
//     float4 tex = tex2D(albedoMap, uvs);
//     material.albedo  = tex.rgb / tex.a;
//     material.opacity = tex.a;
//
// A vertical filmstrip atlas: numFrames stacked frames scrolled at
// framesPerSecond. The wrap address mode makes vOffset roll over for free, so
// no modulo is needed.
//
// The albedo is stored premultiplied and divided back out. That divide blows up
// where tex.a is 0, but every such texel is immediately killed by the alpha
// test, so the NaN never reaches the framebuffer. Preserved as-is rather than
// guarded, since a guard would change the colour of partially transparent
// texels.
//
// twoSided => no culling. See model_alpha_double.shader for the full culling
// rationale; this is the one case where the importer's handedness flip does not
// matter.
//
// No normal or specular is written, so the surface takes lighting on its
// geometric normal with zero specular.
//=============================================================================

HEADER
{
	Description = "NS2 Tentacle Animated";
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

	RenderState( CullMode, NONE );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap, Linear, 8, "None", "_color", ",0/,0/0", Default4( 1.0, 1.0, 1.0, 1.0 ) );

	Texture2D g_tAlbedoMap < Channel( RGBA, Box( AlbedoMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flNumFrames       < UiGroup( ",0/,0/0" ); Default1( 1.0 );  Range1( 1, 256 ); >;
	float g_flFramesPerSecond < UiGroup( ",0/,0/0" ); Default1( 30.0 ); Range1( 0, 120 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		float frame = floor( g_flFramesPerSecond * g_flTime );
		float vOffset = ( 1.0 / g_flNumFrames ) * frame;
		float2 uvs = float2( uv.x, uv.y / g_flNumFrames ) + float2( 0.0, vOffset );

		float4 tex = Tex2DS( g_tAlbedoMap, g_sWrap, uvs );

		m.Opacity = tex.a;
		clip( m.Opacity - 0.5 );
		m.Opacity = 1.0;

		Ns2ApplySpecularGloss( m, ( tex.rgb / tex.a ) * i.vTintColor.rgb, 0.0, 0.0 );

		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = i.vNormalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uvs;

		return ShadingModelStandard::Shade( i, m );
	}
}
