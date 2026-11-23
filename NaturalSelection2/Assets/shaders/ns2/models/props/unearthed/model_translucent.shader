//=============================================================================
// Port of NS2 ns2/models/props/unearthed/model_translucent.surface_shader
//
//     float opacity = 1 - tex2D(opacityMap, input.texCoord).r;
//     float4 specularGloss = tex2D(specularMap, input.texCoord);
//
//     material.wsOffset  = input.wsNormal * -0.0015;
//     material.albedo    = tex2D(albedoMap, input.texCoord).rgb;
//     material.tsNormal  = DecodeNormal( tex2D(normalMap, input.texCoord) );
//     material.specular  = specularGloss.rgb;
//     material.gloss     = specularGloss.a;
//     material.emissive  = tex2D(emissiveMap, input.texCoord).rgb * emissiveMod;
//     material.opacity   = opacity;
//     material.id        = decals;
//     material.transmissionColor = transmissionColor;
//     material.ssDistortion = DecodeNormal( tex2D(normalMap, input.texCoord) ).xy
//                           * lerp( 0.04, 0, 1 - tex2D(opacityMap, input.texCoord).g );
//
// <param name="translucent">true</param> and <param name="wsNormal">true</param>.
//
// The most complete shader in the NS2 set: it is the only one that is
// translucent AND refracts AND displaces. Worth reading carefully because
// several things in it are inverted from the obvious reading.
//
//  - opacity is ONE MINUS the red channel. The map is authored as a hole mask,
//    so white means see-through.
//  - the ssDistortion scale collapses to "0.04 * opacityMap.g" once the double
//    negation in lerp(0.04, 0, 1 - g) is unwound. Green is a separate mask
//    controlling where the glass bends light, independent of red.
//  - wsOffset pushes the surface 0.0015 metres ALONG its own normal, negated,
//    i.e. very slightly inwards. That is a z-fighting fix: the translucent
//    shell sits just inside the opaque model it is paired with.
//
// Because it is translucent, Spark drops WriteDeferred in favour of
// ForwardShadingProbe, so albedo is never shaded - see Ns2ApplyTranslucentGlass
// in ns2.hlsl. Only the specular reflection, the emissive and the refracted
// scene survive, which is why the albedo sample above does not appear below.
//
// material.id carries the decal receive flag, which has no analogue here.
//=============================================================================

HEADER
{
	Description = "NS2 Unearthed Model Translucent";
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
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	// Spark authors wsOffset in metres; s&box works in inches, so -0.0015m.
	float g_flWsOffset < UiGroup( ",0/,0/0" ); Default1( -0.06 ); Range1( -4, 4 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput o = ProcessVertex( v );
		o.vPositionWs.xyz += o.vNormalWs.xyz * g_flWsOffset;
		o.vPositionPs = Position3WsToPs( o.vPositionWs.xyz );
		return FinalizeVertex( o );
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
	#include "ns2/ns2_refract.hlsl"

	BoolAttribute( bWantsFBCopyTexture, true );

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ZERO );
	RenderState( BlendOp, ADD );
	RenderState( SrcBlendAlpha, ONE );
	RenderState( DstBlendAlpha, ZERO );
	RenderState( BlendOpAlpha, ADD );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap,   Srgb,   8, "None",             "_color",    ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap, Linear, 8, "None",             "_spec",     ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( EmissiveMap, Srgb,   8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( OpacityMap,  Linear, 8, "None",             "_opacity",  ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tOpacityMap  < Channel( RGBA, Box( OpacityMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float3 g_vTransmissionColor < UiGroup( ",0/,0/0" ); Default3( 1.0, 1.0, 1.0 ); >;
	float g_flEmissiveMod < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		float2 opacityMap = Tex2DS( g_tOpacityMap, g_sWrap, uv ).rg;
		float opacity = 1.0 - opacityMap.r;

		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );
		float3 emissive = Tex2DS( g_tEmissiveMap, g_sWrap, uv ).rgb * g_flEmissiveMod;

		Ns2ApplyTranslucentGlass( m, specularGloss.rgb, specularGloss.a, emissive, opacity );

		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float3 tsNormal = Ns2DecodeNormal( normal );

		m.Normal = TransformNormal( tsNormal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		// lerp( 0.04, 0, 1 - g ) is just 0.04 * g.
		float2 distortion = tsNormal.xy * ( 0.04 * opacityMap.g );

		float3 refracted = Ns2Refract( i.vPositionSs.xy, i.vPositionSs.z, distortion, g_vTransmissionColor, opacity );

		m.Emission += refracted;

		return ShadingModelStandard::Shade( i, m );
	}
}
