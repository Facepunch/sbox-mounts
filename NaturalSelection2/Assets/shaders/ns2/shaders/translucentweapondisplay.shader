//=============================================================================
// Port of NS2 core/shaders/TranslucentWeaponDisplay.surface_shader
//
//     float darkAreaTransmission   = max(1.0f - baseDarkenFactor, 0.001f);
//     float brightAreaTransmission = max(1.0f - maxDarkenFactor,  0.001f);
//
//     float3 emissive = tex2D(emissiveMap, input.texCoord).rgb;
//     float luminosity = min(max(emissive.r*0.21f + emissive.g*0.72f + emissive.b*0.07f, 0.0f), 1.0f);
//
//     float transmissionFactor = darkAreaTransmission * (1.0f - luminosity) + brightAreaTransmission * luminosity;
//
//     material.transmissionColor = float3(transmissionFactor, transmissionFactor, transmissionFactor);
//     material.emissive = emissive / transmissionFactor;
//     material.refractionExclusion = 1.0f;
//     material.nonOpaqueVelocity = 1;
//
// The LCD panel on the marine weapon viewmodels. transmissionColor is Spark's
// multiplicative transmission term for translucent surfaces, so the composite
// Spark performs is:
//
//     dst = dst * transmissionFactor + emissive
//
// which is a dual-factor blend. s&box expresses that exactly with
// SrcBlend ONE / DstBlend SRC_ALPHA, writing transmissionFactor into alpha:
//
//     dst = emissive * 1 + dst * srcAlpha
//
// Note this is the OPPOSITE assignment to every other emissive port here
// (which use SRC_ALPHA / ONE), because the alpha here is a darkening factor
// rather than a coverage factor.
//
// The emissive is pre-divided by transmissionFactor in NS2 to cancel the fact
// that Spark also darkens the surface's own emissive by transmission. s&box's
// blend does not darken the source, so the divide would double-count.
// It is kept anyway: it is what makes bright screen pixels punch through the
// darkening, and removing it would change the panel's look, not fix it.
//
// refractionExclusion and nonOpaqueVelocity are Spark render-pass hints
// (keep this out of the refraction grab, still write motion vectors). Neither
// has an s&box equivalent and neither affects shading.
//=============================================================================

HEADER
{
	Description = "NS2 Translucent Weapon Display";
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
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, SRC_ALPHA );
	RenderState( BlendOp, ADD );
	RenderState( SrcBlendAlpha, ZERO );
	RenderState( DstBlendAlpha, ONE );
	RenderState( BlendOpAlpha, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissive", ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );

	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flBaseDarkenFactor < UiGroup( ",0/,0/0" ); Default1( 0.85 ); Range1( 0, 1 ); >;
	float g_flMaxDarkenFactor  < UiGroup( ",0/,0/0" ); Default1( 0.95 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float darkAreaTransmission   = max( 1.0 - g_flBaseDarkenFactor, 0.001 );
		float brightAreaTransmission = max( 1.0 - g_flMaxDarkenFactor,  0.001 );

		float3 emissive = Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, i.vTextureCoords.xy ).rgb );

		float luminosity = saturate( dot( emissive, float3( 0.21, 0.72, 0.07 ) ) );

		float transmissionFactor = lerp( darkAreaTransmission, brightAreaTransmission, luminosity );

		return float4( emissive / transmissionFactor, transmissionFactor );
	}
}
