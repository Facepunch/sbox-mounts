//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/enzyme.surface_shader
//
//     const float3 tint = float3(4, 0, 0);
//     float t = time - enzymeTime;                            // computed, never used
//
//     float3 noise  = tex2D( noiseMap, (input.texCoord + float2(time *-.07, time * .1)) * float2(3, 3.5) ).rgb;
//     float3 noise2 = tex2D( noiseMap, (input.texCoord + float2(time * .17, time * -.1)) * float2(5, 5.3) ).rgb;
//
//     material.emissive = (noise2 * noise) * tint + float4(1, 0, 0, 1);
//     material.albedo   = (noise2 * noise) * tint;
//     material.opacity  = (noise2.r * noise.r);
//
// The red enzyme film that coats an infested alien. One noise texture sampled
// twice at different scales and scrolled in opposing directions, multiplied
// together so the film churns rather than slides.
//
// Note the offset is applied BEFORE the scale here - `(texCoord + time*v) *
// scale` - which means the scroll speed is multiplied by the tiling too. The
// small numbers in the offsets are deceptive; the effective motion is 3-5x
// faster than it reads.
//
// emissive adds float4(1,0,0,1) to a float3; Spark drops the extra component,
// so this is just a constant red floor of 1.0 under the noise. That is why the
// film never goes fully black even where both noise layers cancel.
//
// enzymeTime is a gameplay input for a spawn-in animation, and t is computed
// and never used. Ported as authored: the dead term is dropped, and the input
// is kept so materials that set it still bind.
//
// albedo is written, so this takes the full deferred path, and alphaTest is on:
// opacity drives the clip rather than a blend.
//=============================================================================

HEADER
{
	Description = "NS2 Enzyme";
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

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( NoiseMap, Linear, 8, "None", "_noise", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tNoiseMap < Channel( RGBA, Box( NoiseMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flEnzymeTime < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1000 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		const float3 tint = float3( 4.0, 0.0, 0.0 );

		float2 uv = i.vTextureCoords.xy;
		float t = g_flTime;

		// The offset is applied before the scale, so the tiling multiplies the
		// scroll speed as well.
		float3 noise  = Tex2DS( g_tNoiseMap, g_sWrap, ( uv + float2( t * -0.07, t * 0.1 ) ) * float2( 3.0, 3.5 ) ).rgb;
		float3 noise2 = Tex2DS( g_tNoiseMap, g_sWrap, ( uv + float2( t * 0.17, t * -0.1 ) ) * float2( 5.0, 5.3 ) ).rgb;

		float3 film = noise2 * noise;

		Material m = Material::Init();

		// The original writes no specular or gloss, so Spark leaves both at zero.
		Ns2ApplySpecularGloss( m, film * tint, float3( 0.0, 0.0, 0.0 ), 0.0 );

		// float4(1,0,0,1) added to a float3 truncates to a constant red floor.
		m.Emission = film * tint + float3( 1.0, 0.0, 0.0 );

		// Spark truncates the float3 assigned to opacity to .x.
		m.Opacity = film.x;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = i.vNormalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
