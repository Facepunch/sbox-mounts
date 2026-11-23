//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/RS_Monitorscreen.surface_shader
//
//     const float ts = 15.0;
//     const float noisemult = 0.65;
//     const float ScanlineScale = 1.0 / 64;
//
//     float Scanlines = saturate(abs(frac(input.texCoord.y / ScanlineScale + (1-time)) - 0.5) + scanlineIntensity);
//
//     float2 psjitter = floor(time * ts) / ts + (time * 0.028 + float2(-0.005,-0.3));
//     float3 uvjitter = tex2D(noisetex, input.texCoord * 2 + tex2D(noisetex, psjitter).rg).rgb;
//     float2 distortionUV = float2(tex2D(noisetex, float2(input.texCoord.g, psjitter.r)).b, 0) * uvjitter.b * distortionIntensity;
//     float3 screentex = tex2D(albedoMap, input.texCoord - abs(distortionUV)).rgb * saturate(uvjitter.r + noisemult);
//     float3 lcdpattern = tex2D(lcdtex, input.texCoord * 512).rgb * screentex * 3;
//
//     float3 screenblend   = lerp(lcdpattern, screentex, distanceblend(2.0, 2.0, input.vsPosition));
//     float3 scanlineblend = lerp(screenblend * Scanlines, screenblend, distanceblend(4.0, 10.0, input.vsPosition));
//
//     material.albedo   = scanlineblend * albedoStrength * emissiveMod;
//     material.emissive = scanlineblend * emissiveStrength * emissiveMod;
//     material.specular = specularGloss.rgb;
//     material.gloss    = specularGloss.a;
//     material.id       = showDecals;
//
// A CRT/LCD monitor effect. Three things are stacked on the albedo map:
//
//  1. Horizontal tear. psjitter steps at 15 Hz (floor(time*15)/15) with a slow
//     continuous drift added, and is used to look up noisetex twice: once to
//     warp the noise lookup itself, once to pick a per-scanline x offset. The
//     result is subtracted from the UV via abs() so the image only ever tears
//     one direction, and uvjitter.r dims the whole screen during a tear.
//  2. LCD subpixel grid. lcdtex tiled 512x, multiplied in and scaled by 3 to
//     compensate for the average of its mostly-dark pattern.
//  3. Scanlines. A triangle wave over 64 rows scrolling at 1 row/second,
//     lifted by scanlineIntensity so it never fully blacks out.
//
// Both 2 and 3 are distance-faded to kill moire: the LCD grid blends out
// between 2m and 4m, the scanlines between 10m and 14m, after which the screen
// is just the plain texture. distanceblend measures view-space distance, so we
// use length(vPositionWithOffsetWs) which is already camera-relative, scaled
// by 1/40 because Model.cs imports NS2 metres at 40 units each.
//
// NS2 declares its own frac() identical to the intrinsic, and computes a
// worldPos from wsPosition.z that is never used; both are dropped here.
//=============================================================================

HEADER
{
	Description = "NS2 Monitor Screen";
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

	CreateInputTexture2D( AlbedoMap,   Linear, 8, "None",             "_color",  ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal", ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap, Linear, 8, "None",             "_spec",   ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( NoiseTex,    Linear, 8, "None",             "_noise",  ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( LcdTex,      Linear, 8, "None",             "_lcd",    ",0/,0/0", Default3( 0.333, 0.333, 0.333 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNoisetex    < Channel( RGBA, Box( NoiseTex ),    Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tLcdtex      < Channel( RGBA, Box( LcdTex ),      Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flEmissiveStrength    < UiGroup( ",0/,0/0" ); Default1( 1.0 );  Range1( 0, 100 ); >;
	float g_flEmissiveMod         < UiGroup( ",0/,0/0" ); Default1( 1.0 );  Range1( 0, 100 ); >;
	float g_flAlbedoStrength      < UiGroup( ",0/,0/0" ); Default1( 1.0 );  Range1( 0, 100 ); >;
	float g_flDistortionIntensity < UiGroup( ",0/,0/0" ); Default1( 0.01 ); Range1( 0, 1 ); >;
	float g_flScanlineIntensity   < UiGroup( ",0/,0/0" ); Default1( 0.8 );  Range1( 0, 1 ); >;
	float g_flShowDecals          < UiGroup( ",0/,0/0" ); Default1( 0.0 );  Range1( 0, 1 ); >;

	static const float NS2_UNITS_PER_METRE = 40.0;

	float DistanceBlend( float range, float offset, float distanceMetres )
	{
		return saturate( ( distanceMetres - offset ) / range );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		const float ts = 15.0;
		const float noisemult = 0.65;
		const float ScanlineScale = 1.0 / 64.0;

		float2 uv = i.vTextureCoords.xy;
		float distanceMetres = length( i.vPositionWithOffsetWs.xyz ) / NS2_UNITS_PER_METRE;

		float scanlines = saturate( abs( frac( uv.y / ScanlineScale + ( 1.0 - g_flTime ) ) - 0.5 ) + g_flScanlineIntensity );

		float2 psjitter = floor( g_flTime * ts ) / ts + ( g_flTime * 0.028 + float2( -0.005, -0.3 ) );
		float3 uvjitter = Tex2DS( g_tNoisetex, g_sWrap, uv * 2.0 + Tex2DS( g_tNoisetex, g_sWrap, psjitter ).rg ).rgb;
		float2 distortionUV = float2( Tex2DS( g_tNoisetex, g_sWrap, float2( uv.g, psjitter.r ) ).b, 0.0 ) * uvjitter.b * g_flDistortionIntensity;

		float3 screentex = Tex2DS( g_tAlbedoMap, g_sWrap, uv - abs( distortionUV ) ).rgb * saturate( uvjitter.r + noisemult );
		float3 lcdpattern = Tex2DS( g_tLcdtex, g_sWrap, uv * 512.0 ).rgb * screentex * 3.0;

		float3 screenblend = lerp( lcdpattern, screentex, DistanceBlend( 2.0, 2.0, distanceMetres ) );
		float3 scanlineblend = lerp( screenblend * scanlines, screenblend, DistanceBlend( 4.0, 10.0, distanceMetres ) );

		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );

		Ns2ApplySpecularGloss( m, scanlineblend * g_flAlbedoStrength * g_flEmissiveMod, specularGloss.rgb, specularGloss.a );

		m.Emission = Ns2SrgbToLinear( saturate( scanlineblend ) ) * g_flEmissiveStrength * g_flEmissiveMod;
		m.Opacity = 1.0;
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = TransformNormal( Ns2DecodeNormal( normal ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
