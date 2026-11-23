//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/RS_Monitorscreen_animated.surface_shader
//
//     const float ts = animationspeed;
//     float2 psjitter = floor(time * ts) / ts + (time * 0.024 + float2(-0.005,-0.3));
//     ...
//     float timestephard = floor(time * animationspeed);
//     float timestepsoft = time * animationspeed;
//     float timesteps    = lerp(timestephard, timestepsoft, smoothBlending);
//     float2 frames      = float2(floor(horizontalFrames), floor(verticalFrames));
//     float2 framesdiv   = float2(1,1) / frames;
//     float substeps     = timesteps - frac(timesteps);
//     float2 sub1 = float2(fmod(substeps,   frames.r), floor(substeps   * framesdiv.r));
//     float2 sub2 = float2(fmod(1+substeps, frames.r), floor((1+substeps) * framesdiv.r));
//     float2 UV1 = (input.texCoord + sub1) * framesdiv - abs(distortionUV);
//     float2 UV2 = (input.texCoord + sub2) * framesdiv - abs(distortionUV);
//     float3 SubUVlerp = lerp(tex2D(albedoMap, UV1).rgb, tex2D(albedoMap, UV2).rgb, frac(timesteps))
//                      * saturate(uvjitter.r + noisemult);
//
// RS_Monitorscreen with the single screen texture replaced by a sprite sheet
// flipbook. See cinematics/vfx_materials/rs_monitorscreen.shader for the tear
// / LCD grid / scanline breakdown, which is unchanged apart from three details:
//
//  - the jitter step rate ts is animationspeed rather than a fixed 15, and the
//    slow drift is 0.024 rather than 0.028
//  - emissiveStrength defaults to 0, so these screens are lit surfaces by
//    default rather than glowing ones
//  - the plain screentex sample is gone entirely; SubUVlerp takes its place
//    everywhere, including as the input to the LCD grid multiply
//
// smoothBlending picks between a hard frame step (floor) and a continuous one,
// and frac(timesteps) cross-fades frame N into frame N+1. With the defaults
// (1x1 frames) sub1 is (0, floor(t)) and framesdiv is 1, so both lookups land
// on the same wrapped texel and the flipbook collapses to a static image.
//=============================================================================

HEADER
{
	Description = "NS2 Monitor Screen Animated";
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

	float g_flEmissiveStrength    < UiGroup( ",0/,0/0" ); Default1( 0.0 );  Range1( 0, 100 ); >;
	float g_flEmissiveMod         < UiGroup( ",0/,0/0" ); Default1( 1.0 );  Range1( 0, 100 ); >;
	float g_flAlbedoStrength      < UiGroup( ",0/,0/0" ); Default1( 1.0 );  Range1( 0, 100 ); >;
	float g_flDistortionIntensity < UiGroup( ",0/,0/0" ); Default1( 0.01 ); Range1( 0, 1 ); >;
	float g_flScanlineIntensity   < UiGroup( ",0/,0/0" ); Default1( 0.8 );  Range1( 0, 1 ); >;
	float g_flShowDecals          < UiGroup( ",0/,0/0" ); Default1( 0.0 );  Range1( 0, 1 ); >;
	float g_flAnimationspeed      < UiGroup( ",0/,0/0" ); Default1( 1.0 );  Range1( 0, 64 ); >;
	float g_flHorizontalFrames    < UiGroup( ",0/,0/0" ); Default1( 1.0 );  Range1( 1, 64 ); >;
	float g_flVerticalFrames      < UiGroup( ",0/,0/0" ); Default1( 1.0 );  Range1( 1, 64 ); >;
	float g_flSmoothBlending      < UiGroup( ",0/,0/0" ); Default1( 1.0 );  Range1( 0, 1 ); >;

	static const float NS2_UNITS_PER_METRE = 40.0;

	float DistanceBlend( float range, float offset, float distanceMetres )
	{
		return saturate( ( distanceMetres - offset ) / range );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		const float noisemult = 0.65;
		const float ScanlineScale = 1.0 / 64.0;

		float ts = g_flAnimationspeed;
		float2 uv = i.vTextureCoords.xy;
		float distanceMetres = length( i.vPositionWithOffsetWs.xyz ) / NS2_UNITS_PER_METRE;

		float scanlines = saturate( abs( frac( uv.y / ScanlineScale + ( 1.0 - g_flTime ) ) - 0.5 ) + g_flScanlineIntensity );

		float2 psjitter = floor( g_flTime * ts ) / ts + ( g_flTime * 0.024 + float2( -0.005, -0.3 ) );
		float3 uvjitter = Tex2DS( g_tNoisetex, g_sWrap, uv * 2.0 + Tex2DS( g_tNoisetex, g_sWrap, psjitter ).rg ).rgb;
		float2 distortionUV = float2( Tex2DS( g_tNoisetex, g_sWrap, float2( uv.g, psjitter.r ) ).b, 0.0 ) * uvjitter.b * g_flDistortionIntensity;

		float timestephard = floor( g_flTime * g_flAnimationspeed );
		float timestepsoft = g_flTime * g_flAnimationspeed;
		float timesteps = lerp( timestephard, timestepsoft, g_flSmoothBlending );

		float2 frames = float2( floor( g_flHorizontalFrames ), floor( g_flVerticalFrames ) );
		float2 framesdiv = float2( 1.0, 1.0 ) / frames;

		float substeps = timesteps - frac( timesteps );

		float2 sub1 = float2( fmod( substeps, frames.r ), floor( substeps * framesdiv.r ) );
		float2 sub2 = float2( fmod( 1.0 + substeps, frames.r ), floor( ( 1.0 + substeps ) * framesdiv.r ) );

		float2 UV1 = ( uv + sub1 ) * framesdiv - abs( distortionUV );
		float2 UV2 = ( uv + sub2 ) * framesdiv - abs( distortionUV );

		float3 subUvLerp = lerp(
			Tex2DS( g_tAlbedoMap, g_sWrap, UV1 ).rgb,
			Tex2DS( g_tAlbedoMap, g_sWrap, UV2 ).rgb,
			frac( timesteps ) ) * saturate( uvjitter.r + noisemult );

		float3 lcdpattern = Tex2DS( g_tLcdtex, g_sWrap, uv * 512.0 ).rgb * subUvLerp * 3.0;

		float3 screenblend = lerp( lcdpattern, subUvLerp, DistanceBlend( 2.0, 2.0, distanceMetres ) );
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
