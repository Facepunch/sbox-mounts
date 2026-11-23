#ifndef NS2_HLSL
#define NS2_HLSL

// Match the mount geometry scale for shader distances authored in metres.
#ifndef NS2_METRE
#define NS2_METRE 40.0
#endif

//=============================================================================
// Shared helpers for the ported NS2 (Spark) surface shaders.
//
// Reverse engineered from core/renderer/Deferred.shader_template,
// DeferredShading.hlsl, Lighting.hlsl and ToneMap.hlsl.
//
// Spark is a Blinn-Phong deferred renderer with a specular/gloss workflow:
//
//     BlinnPhong( radiance, n, l, v, albedo, specular, gloss * 256 )
//
// s&box is a linear PBR renderer with a metalness/roughness workflow, so every
// port converts spec/gloss to metal/rough with the standard glTF solver below.
//
// Mounted UNORM textures with an sRGB input are decoded when sampled.
// The shared lighting conversion also decodes raw albedo and specular inputs.
//=============================================================================

#define NS2_DIELECTRIC_SPECULAR 0.04

float3 Ns2SrgbToLinear( float3 c )
{
	return c * ( c * ( c * 0.305306011 + 0.682171111 ) + 0.012522878 );
}

// Spark: float4 n = texNormal * 2 - 1; return n.xyz;
float3 Ns2DecodeNormal( float4 texNormal )
{
	return texNormal.xyz * 2.0 - 1.0;
}

// Blinn-Phong exponent (gloss * 256) to GGX roughness.
// n = 2 / a^2 - 2  =>  a = sqrt( 2 / (n + 2) ),  roughness = sqrt( a )
float Ns2GlossToRoughness( float gloss )
{
	return pow( 2.0 / ( gloss * 256.0 + 2.0 ), 0.25 );
}

float Ns2SolveMetalness( float diffuse, float specular )
{
	if ( specular < NS2_DIELECTRIC_SPECULAR )
		return 0.0;

	float oneMinusSpecularStrength = 1.0 - specular;
	float a = NS2_DIELECTRIC_SPECULAR;
	float b = diffuse * oneMinusSpecularStrength / ( 1.0 - NS2_DIELECTRIC_SPECULAR )
		+ specular - 2.0 * NS2_DIELECTRIC_SPECULAR;
	float c = NS2_DIELECTRIC_SPECULAR - specular;

	return saturate( ( -b + sqrt( max( b * b - 4.0 * a * c, 0.0 ) ) ) / ( 2.0 * a ) );
}

// Folds Spark's albedo + specular colour + gloss into s&box's Albedo,
// Metalness and Roughness so the lit result matches as closely as the two
// shading models allow.
void Ns2ApplySpecularGloss( inout Material m, float3 albedo, float3 specular, float gloss )
{
	albedo = Ns2SrgbToLinear( saturate( albedo ) );
	specular = Ns2SrgbToLinear( saturate( specular ) );

	float specularStrength = max( specular.r, max( specular.g, specular.b ) );
	float diffuseStrength = max( albedo.r, max( albedo.g, albedo.b ) );
	float metalness = Ns2SolveMetalness( diffuseStrength, specularStrength );

	float oneMinusSpecularStrength = 1.0 - specularStrength;
	float3 fromDiffuse = albedo * ( oneMinusSpecularStrength / ( 1.0 - NS2_DIELECTRIC_SPECULAR ) )
		/ max( 1.0 - metalness, 1e-4 );
	float3 fromSpecular = ( specular - NS2_DIELECTRIC_SPECULAR * ( 1.0 - metalness ) )
		/ max( metalness, 1e-4 );

	m.Albedo = saturate( lerp( fromDiffuse, fromSpecular, metalness * metalness ) );
	m.Metalness = metalness;
	m.Roughness = Ns2GlossToRoughness( gloss );
}

//=============================================================================
// Translucent NS2 materials.
//
// A surface_shader with <param name="translucent">true</param> is NOT lit by
// the deferred pass. Deferred.shader_template swaps WriteDeferred out for
// ForwardShadingProbe, and Deferred.hlsl's ForwardShadingProbePS ends with:
//
//     float3 env = DecodeRGBE( texCUBEbias( environmentTexture, ... ) ) * attenuation;
//     return float4( env * material.specular * probeTint.rgb, attenuation );
//
// blended with src_color_factor = one, dst_color_factor = one. Two further
// additive/multiplicative passes contribute, so the full composite is:
//
//     dst = dst * lerp( 1, transmissionColor, opacity )   // WriteTransmission, dst_factor = src_color
//         + emissive * opacity                            // WriteEmissive,     src_factor = src_alpha
//         + env( reflect, gloss ) * specular              // ForwardShadingProbe
//
// Three consequences that are easy to get wrong:
//
//  1. material.albedo is DEAD for translucent materials - there is no diffuse
//     term at all. biodome_glass setting albedo to black is not a hint that the
//     glass is dark, it is simply an unused output.
//  2. opacity does NOT hide the background. It only scales the emissive and
//     picks how far to lerp towards transmissionColor, so with the default
//     transmissionColor of (1,1,1) an opacity map is inert except on emissive.
//  3. The surface is therefore ADDITIVE. Porting one of these with a normal
//     alpha blend turns transparent glass into a flat black pane.
//
// So the ports below use SrcBlend/DstBlend ONE and rely on Shade() returning
// unpremultiplied colour - with Albedo driven to metal by the solver above the
// only things left in that colour are the specular reflection and Emission.
//
// The transmission multiply cannot share a pass with an additive blend. Every
// shader in this family leaves transmissionColor at its (1,1,1) default, which
// makes the multiply an identity, so it is dropped rather than faked.
//=============================================================================
void Ns2ApplyTranslucentGlass( inout Material m, float3 specular, float gloss, float3 emissive, float opacity )
{
	Ns2ApplySpecularGloss( m, float3( 0.0, 0.0, 0.0 ), specular, gloss );

	m.Emission = emissive * opacity;
	m.Opacity = 1.0;
	m.AmbientOcclusion = 1.0;
	m.TintMask = 1.0;
}

#endif // NS2_HLSL
