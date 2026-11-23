//=============================================================================
// Port of NS2 ns2/materials/unearthed/unearthed_goofall.surface_shader
//
//     const float2 FlowSpeed  = float2(0.01,-0.05);
//     const float2 NoiseSpeed = float2(0.02,-0.05);
//     const float Multz = 0.75;
//     const float Addz  = -0.375;
//
//     float2 UVScroll = (UV * float2(1,2))   + (time * FlowSpeed);
//     float2 UVNoise  = (UV * float2(1.5,0.5)) + (time * NoiseSpeed);
//
//     float Noise = 1 - tex2D(NoiseTexture, UVNoise).r * (UV.g * 2 * Multz + Addz);
//     float3 EmissiveMask = (tex2D(GooTexture, UVScroll).rgb * Tint) * floor(Noise.r + 0.5);
//
//     material.albedo   = tex2D(GooTexture, UVScroll).rgb * Tint;
//     material.emissive = EmissiveMask.rgb;
//     material.tsNormal = tex2D(NormalTexture, UVScroll).rgb;
//     material.specular = tex2D(SpecularTexture, UVScroll).rgb;
//     material.gloss    = .9;
//     material.opacity  = saturate(Noise.r);
//
// A sheet of goo running down a wall - the falling sibling of unearthed_goop,
// which pools instead. Where goop uses a flowmap to swirl, this one just scrolls
// and relies on a noise mask for its shape.
//
// The Noise expression is the whole shader. `UV.g * 2 * 0.75 - 0.375` is a
// vertical ramp running from -0.375 at the top to +1.125 at the bottom, and the
// noise texture is multiplied by it before being subtracted from 1. So the mask
// is fully opaque at the top and progressively eaten away towards the bottom -
// the goo thins out as it falls, and the noise decides where it breaks up. The
// negative value at the very top means the noise briefly ADDS there, pushing
// Noise above 1, which saturate() then flattens into solid coverage.
//
// floor(Noise + 0.5) rounds to 0 or 1, so the emissive is a hard binary mask
// while the opacity is the smooth version of the same value. The result glows
// only where the goo is thick enough to be solid.
//
// Note tsNormal is the raw texture sample with no decode at all - the original
// does not call DecodeNormal here, so the normal stays in 0..1 rather than
// -1..1. That biases every normal towards +Z and makes the surface read flatter
// than it should. Ported as authored.
//
// albedo is written, so this takes the full deferred path, and alphaTest is on.
//=============================================================================

HEADER
{
	Description = "NS2 Unearthed Goofall";
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

	// twoSided in the original.
	RenderState( CullMode, NONE );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( GooTexture,      Srgb,   8, "None", "_color",  ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NoiseTexture,    Linear, 8, "None", "_noise",  ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NormalTexture,   Linear, 8, "None", "_normal", ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularTexture, Linear, 8, "None", "_spec",   ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tGooTexture      < Channel( RGBA, Box( GooTexture ),      Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tNoiseTexture    < Channel( RGBA, Box( NoiseTexture ),    Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalTexture   < Channel( RGBA, Box( NormalTexture ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularTexture < Channel( RGBA, Box( SpecularTexture ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float3 g_vTint < UiGroup( ",0/,0/0" ); Default3( 1.0, 1.0, 1.0 ); >;
	float g_flDecals < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		const float2 flowSpeed = float2( 0.01, -0.05 );
		const float2 noiseSpeed = float2( 0.02, -0.05 );
		const float multz = 0.75;
		const float addz = -0.375;

		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		float2 uvScroll = ( uv * float2( 1.0, 2.0 ) ) + ( t * flowSpeed );
		float2 uvNoise  = ( uv * float2( 1.5, 0.5 ) ) + ( t * noiseSpeed );

		// A vertical ramp from -0.375 at the top to +1.125 at the bottom, so the
		// goo is solid at the top and breaks up as it falls.
		float noise = 1.0 - Tex2DS( g_tNoiseTexture, g_sWrap, uvNoise ).r * ( uv.g * 2.0 * multz + addz );

		float3 goo = Tex2DS( g_tGooTexture, g_sWrap, uvScroll ).rgb * g_vTint;
		float3 specular = Tex2DS( g_tSpecularTexture, g_sWrap, uvScroll ).rgb;

		// Raw sample, no decode - the original does not call DecodeNormal here.
		float3 normal = Tex2DS( g_tNormalTexture, g_sWrap, uvScroll ).rgb;

		Material m = Material::Init();

		Ns2ApplySpecularGloss( m, goo, specular, 0.9 );

		// Hard binary mask for the glow, smooth version for the coverage.
		m.Emission = goo * floor( noise + 0.5 );
		m.Opacity = saturate( noise );
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = TransformNormal( normal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
