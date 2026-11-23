//=============================================================================
// Port of NS2 ns2/models/marine/command_station/command_station_display.surface_shader
//
//     float f = 1 - hiddenAmount;
//     float4 tex;
//     if (state == 1) tex = tex2D(emissiveMap, input.texCoord);
//     else if (state == 2) tex = tex2D(lockedEmissiveMap, input.texCoord);
//     else if (state == 3) tex = tex2D(welcomeEmissiveMap, input.texCoord);
//
//     if (state == 5)
//     {
//         material.emissive = float3(0, 0, 0);
//         material.opacity  = 0;
//     }
//     else
//     {
//         material.emissive = tex.rgb * input.color.rgb * (f + 1 + cos(time * 4));
//         material.opacity  = tex.a * input.color.a;
//     }
//
// Only emissive + opacity are written, so this runs Spark's WriteEmissive
// technique: additive, premultiplied by opacity.
//
// NS2 leaves `tex` uninitialised for any state that is not 1, 2, 3 or 5, which
// is undefined behaviour. We initialise it to zero so unknown states are
// simply invisible rather than reading garbage.
//
// The timeStateChanged input is declared in the NS2 source and never read.
// It is kept so materials that set it still bind, but it is inert.
//
// The pulse (f + 1 + cos(time*4)) swings between roughly 0 and 2 at the
// default hiddenAmount of 0, i.e. the display breathes once every ~1.6s.
//=============================================================================

HEADER
{
	Description = "NS2 Command Station Display";
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
	RenderState( DstBlend, ONE );
	RenderState( BlendOp, ADD );
	RenderState( SrcBlendAlpha, SRC_ALPHA );
	RenderState( DstBlendAlpha, ONE );
	RenderState( BlendOpAlpha, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( EmissiveMap,        Linear, 8, "None", "_emissive", ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( WelcomeEmissiveMap, Linear, 8, "None", "_emissive", ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( LockedEmissiveMap,  Linear, 8, "None", "_emissive", ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );

	Texture2D g_tEmissiveMap        < Channel( RGBA, Box( EmissiveMap ),        Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tWelcomeEmissiveMap < Channel( RGBA, Box( WelcomeEmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tLockedEmissiveMap  < Channel( RGBA, Box( LockedEmissiveMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flHiddenAmount    < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flState           < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 8 ); >;
	float g_flTimeStateChanged < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1000 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;

		float f = 1.0 - g_flHiddenAmount;

		float4 tex = 0.0;
		if ( g_flState == 1.0 )      tex = Tex2DS( g_tEmissiveMap, g_sWrap, uv );
		else if ( g_flState == 2.0 ) tex = Tex2DS( g_tLockedEmissiveMap, g_sWrap, uv );
		else if ( g_flState == 3.0 ) tex = Tex2DS( g_tWelcomeEmissiveMap, g_sWrap, uv );

		if ( g_flState == 5.0 )
			return float4( 0.0, 0.0, 0.0, 0.0 );

		float3 emissive = Ns2SrgbToLinear( tex.rgb ) * i.vColor.rgb * ( f + 1.0 + cos( g_flTime * 4.0 ) );
		float opacity = tex.a * i.vColor.a;

		return float4( emissive, opacity );
	}
}
