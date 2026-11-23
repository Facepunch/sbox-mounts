//=============================================================================
// Port of NS2 ns2/models/props/refinery/digital_infestation.surface_shader
//
//     float smoothstep(float t)
//     {
//         return (3 - 2 * t) * t * t;
//     }
//
//     void MaterialShader(in Material_Input input, inout Material material)
//     {
//         const float  speed      = 0.5;
//         const float  blockSize  = 1.0 / 16.0;
//         const float3 color      = float3(0, 2.25, 1.125);
//
//         float t = time * speed;
//
//         // Create blocks.
//         float2 texCoord = input.texCoord;
//         texCoord = floor(texCoord / blockSize) * blockSize;
//
//         // Add scan lines.
//         float  scanLine = cos(input.texCoord.y * 250 + t * 20) + 1;
//         float3 bgColor  = color * scanLine * 0.01;
//
//         float infest  = tex2D(infestMap, texCoord * 0.25 + float2(0, t * 0.01)).r;
//         float mask    = pow( 2 * infest, 0.5 );
//         float3 blocks = tex2D(blocksMap, texCoord * 2);
//         float opacity =  input.texCoord.y + cos(t * 3 + texCoord.x * 2 + sin(t * 2)) * 0.5 + 1 + sin(t * 0.1) * 0.4;
//
//         float threshold = 0.5;
//         float amount1 = saturate((opacity - mask) - threshold);
//         float amount2 = pow( smoothstep( saturate(opacity- mask) ), 2);
//
//         material.albedo   = float3(0, 0, 0);
//         material.emissive = ((amount2 - amount1 * 0.75) * color + bgColor) * emissiveMod;
//     }
//
// Notes on the port:
//
//  * blocksMap is sampled into `blocks` and then never used - the input is
//    declared and shipped with the material but the result is dead. Dropped
//    here rather than carried as an unused texture.
//
//  * `smoothstep(float)` is a one-argument function that shadows nothing in
//    HLSL (the intrinsic takes three), so it is a genuine helper. Renamed to
//    InfestSmoothstep so it can't collide with the intrinsic.
//
//  * `color` is float3(0, 2.25, 1.125), an over-unity constant written straight
//    into Spark's linear lighting buffer by WriteEmissivePS - ToneMap.hlsl only
//    gamma-encodes at the very end. It is already linear, so unlike an emissive
//    *texture* (which NS2 marks srgb="true" and the hardware decodes) it must
//    NOT be run through Ns2SrgbToLinear. infestMap is not srgb either, and only
//    its red channel is used as a mask, so it stays raw as well.
//
//  * The shader writes material.albedo (albedoSpecularGloss group) and
//    material.emissive (emissive group), so Spark runs both WriteDeferred and
//    WriteEmissive: opaque black in the G-buffer with the glow added on top.
//    Reproduced here as an ordinary opaque surface with Albedo 0 and Emission
//    set, which lands in the same place. material.opacity is never written and
//    there is no <params> block, so there is no alpha test and no blending.
//=============================================================================

HEADER
{
	Description = "NS2 Digital Infestation";
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
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( InfestMap, Linear, 8, "None", "_mask", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tInfestMap < Channel( RGBA, Box( InfestMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flEmissiveMod < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 8 ); >;

	float InfestSmoothstep( float t )
	{
		return ( 3.0 - 2.0 * t ) * t * t;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		const float speed = 0.5;
		const float blockSize = 1.0 / 16.0;
		const float3 color = float3( 0.0, 2.25, 1.125 );

		float t = g_flTime * speed;

		float2 uv = i.vTextureCoords.xy;
		float2 texCoord = floor( uv / blockSize ) * blockSize;

		float scanLine = cos( uv.y * 250.0 + t * 20.0 ) + 1.0;
		float3 bgColor = color * scanLine * 0.01;

		float infest = Tex2DS( g_tInfestMap, g_sWrap, texCoord * 0.25 + float2( 0.0, t * 0.01 ) ).r;
		float mask = pow( 2.0 * infest, 0.5 );

		float opacity = uv.y + cos( t * 3.0 + texCoord.x * 2.0 + sin( t * 2.0 ) ) * 0.5 + 1.0 + sin( t * 0.1 ) * 0.4;

		const float threshold = 0.5;
		float amount1 = saturate( ( opacity - mask ) - threshold );
		float amount2 = pow( InfestSmoothstep( saturate( opacity - mask ) ), 2.0 );

		m.Albedo = 0.0;
		m.Emission = ( ( amount2 - amount1 * 0.75 ) * color + bgColor ) * g_flEmissiveMod;

		m.Opacity = 1.0;
		m.Roughness = 1.0;
		m.Metalness = 0.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = i.vNormalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
