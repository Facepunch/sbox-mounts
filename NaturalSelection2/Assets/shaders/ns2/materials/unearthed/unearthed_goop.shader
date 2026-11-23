//=============================================================================
// Port of NS2 ns2/materials/unearthed/unearthed_goop.surface_shader
//
//     const float Period = 1;
//     float2 UV = input.texCoord;
//     float2 worldUV = input.wsPosition.xz * SurfaceScale;
//
//     float basetime = (time * FlowSpeed) * .02;
//     float noisytime = (tex2D(FlowmapNoise, frac(UV + basetime)).r * .25) + (time * FlowSpeed);
//
//     float2 flowmapdirection = (tex2D(FlowmapTexture, UV).rg * float2(1,1) - float2(.4,.4));
//     float2 sample1uvs = (frac(noisytime) * flowmapdirection) + worldUV;
//     float2 sample2uvs = (frac(noisytime - .5) * flowmapdirection) + worldUV + .5;
//     float3 sample1 = tex2D(GooTexture, sample1uvs);
//     float3 sample2 = tex2D(GooTexture, sample2uvs);
//
//     float sawtoothbase = frac(frac(noisytime) / Period);
//     float sawtooth = lerp(sawtoothbase * 2, (1-sawtoothbase) * 2, floor(sawtoothbase*2));
//
//     float3 flowmapblend = lerp(sample2, sample1, sawtooth);
//     float3 normalsample1 = tex2D(NormalTexture, sample1uvs);
//     float3 normalsample2 = tex2D(NormalTexture, sample2uvs);
//     float3 flowmapnormalblend = lerp(normalsample2, normalsample1, sawtooth);
//
//     material.albedo   = flowmapblend.rgb * Tint;
//     material.emissive = flowmapblend.rgb * Tint * 1;
//     material.tsNormal = flowmapnormalblend;
//     material.specular = .4 * flowmapblend;
//     material.gloss    = .6;
//     material.wsOffset = input.wsNormal * (sin(time * 0.4) * WaveIntensity);
//     material.id       = decals;
//
// A textbook flowmap: the goo texture is sampled twice along the flow
// direction at half-cycle offsets and cross-faded, so the UVs can advect
// forever without visibly stretching. The pair is reset every cycle and the
// sawtooth hides the reset.
//
// Two details are easy to get wrong:
//  - the sawtooth is a triangle wave, not a ramp. lerp(x*2, (1-x)*2, floor(x*2))
//    picks the rising half below 0.5 and the falling half above, so the blend
//    goes 0 -> 1 -> 0 across the cycle and the two samples swap roles smoothly.
//  - flowmapdirection subtracts 0.4, not 0.5, so a neutral grey flowmap still
//    drifts slightly. That bias is authored in and is kept.
//
// The normal map is used RAW - the original never calls DecodeNormal on it, so
// it is read as a 0..1 vector rather than being expanded to -1..1. That is
// almost certainly a bug in the original, but it is what the art was tuned
// against, so it is reproduced here.
//
// Coordinate systems differ: Spark is Y-up, so its horizontal plane is XZ.
// s&box is Z-up, so the equivalent world-space projection is XY.
//=============================================================================

HEADER
{
	Description = "NS2 Unearthed Goop";
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

	float g_flWaveIntensity < UiGroup( ",0/,0/0" ); Default1( 0.02 ); Range1( 0, 16 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput o = ProcessVertex( v );

		// Spark: material.wsOffset = wsNormal * (sin(time * 0.4) * WaveIntensity),
		// authored in metres.
		o.vPositionWs.xyz += o.vNormalWs.xyz * ( sin( g_flTime * 0.4 ) * g_flWaveIntensity * 40.0 );
		o.vPositionPs = Position3WsToPs( o.vPositionWs.xyz );

		return FinalizeVertex( o );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	RenderState( CullMode, NONE );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( GooTexture,     Srgb,   8, "None", "_color",  ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( FlowmapTexture, Linear, 8, "None", "_flow",   ",0/,0/0", Default3( 0.4, 0.4, 0.0 ) );
	CreateInputTexture2D( FlowmapNoise,   Linear, 8, "None", "_noise",  ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NormalTexture,  Linear, 8, "None", "_normal", ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );

	Texture2D g_tGooTexture     < Channel( RGBA, Box( GooTexture ),     Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tFlowmapTexture < Channel( RGBA, Box( FlowmapTexture ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tFlowmapNoise   < Channel( RGBA, Box( FlowmapNoise ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalTexture  < Channel( RGBA, Box( NormalTexture ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float3 g_vTint < UiGroup( ",0/,0/0" ); Default3( 1.0, 1.0, 1.0 ); >;
	float g_flFlowSpeed < UiGroup( ",0/,0/0" ); Default1( 0.5 ); Range1( -8, 8 ); >;
	float g_flSurfaceScale < UiGroup( ",0/,0/0" ); Default1( 0.25 ); Range1( 0, 16 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;
		float3 vPositionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		// Spark projects onto its horizontal plane, XZ in a Y-up world.
		float2 worldUv = vPositionWs.xy * g_flSurfaceScale;

		float basetime = ( g_flTime * g_flFlowSpeed ) * 0.02;
		float noisytime = ( Tex2DS( g_tFlowmapNoise, g_sWrap, frac( uv + basetime ) ).r * 0.25 ) + ( g_flTime * g_flFlowSpeed );

		float2 flowmapdirection = Tex2DS( g_tFlowmapTexture, g_sWrap, uv ).rg - 0.4;

		float2 sample1uvs = frac( noisytime ) * flowmapdirection + worldUv;
		float2 sample2uvs = frac( noisytime - 0.5 ) * flowmapdirection + worldUv + 0.5;

		float3 sample1 = Tex2DS( g_tGooTexture, g_sWrap, sample1uvs ).rgb;
		float3 sample2 = Tex2DS( g_tGooTexture, g_sWrap, sample2uvs ).rgb;

		// Period is a compile time constant of 1, so frac( x / Period ) is frac( x ),
		// and frac of a frac is a no-op.
		float sawtoothbase = frac( noisytime );
		float sawtooth = lerp( sawtoothbase * 2.0, ( 1.0 - sawtoothbase ) * 2.0, floor( sawtoothbase * 2.0 ) );

		float3 flowmapblend = lerp( sample2, sample1, sawtooth );

		float3 normalsample1 = Tex2DS( g_tNormalTexture, g_sWrap, sample1uvs ).rgb;
		float3 normalsample2 = Tex2DS( g_tNormalTexture, g_sWrap, sample2uvs ).rgb;
		float3 flowmapnormalblend = lerp( normalsample2, normalsample1, sawtooth );

		float3 albedo = flowmapblend * g_vTint;

		Ns2ApplySpecularGloss( m, albedo, 0.4 * flowmapblend, 0.6 );

		m.Emission = albedo;
		m.Normal = TransformNormal( flowmapnormalblend, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
