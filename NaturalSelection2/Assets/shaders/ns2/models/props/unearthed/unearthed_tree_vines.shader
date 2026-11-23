//=============================================================================
// Port of NS2 ns2/models/props/unearthed/unearthed_tree_vines.surface_shader
//
//     // VERTEX SHADER
//     const float swayStartV = 0.1f;  const float swayEndV = 0.9f;
//     const float swayFrequency = 0.18f;  const float swayAmp = 0.25f;
//
//     float thetaOffset = input.wsPosition.y * 30.0f;
//     float theta     = time     * swayFrequency * 2 * pi + thetaOffset;
//     float prevTheta = prevTime * swayFrequency * 2 * pi + thetaOffset;
//
//     float swayIntensity = saturate( (input.texCoord.y - swayStartV) / (swayEndV - swayStartV) ) * swayAmp;
//
//     material.wsOffset     = float3( sin(theta),     0, cos(theta)     ) * swayIntensity;
//     material.prevWsOffset = float3( sin(prevTheta), 0, cos(prevTheta) ) * swayIntensity;
//
//     // FRAGMENT SHADER
//     float4 specularGloss = tex2D(specularMap, input.texCoord);
//     material.albedo   = tex2D(albedoMap, input.texCoord).rgb;
//     material.tsNormal = DecodeNormal( tex2D(normalMap, input.texCoord) );
//     material.specular = specularGloss.rgb;
//     material.gloss    = specularGloss.a;
//     material.emissive = tex2D(emissiveMap, input.texCoord).rgb * emissiveMod;
//     material.opacity  = tex2D(opacityMap, input.texCoord).r;
//     material.id       = decals;
//
// <param name="alphaTest">true</param> and <param name="twoSided">true</param>.
//
// Alpha tested foliage with a wind sway. The sway is a circle in the
// horizontal plane rather than a back-and-forth, which reads as a lazy drift
// rather than a flap.
//
// Two things carry meaning:
//  - swayIntensity ramps from 0 to swayAmp over V from 0.1 to 0.9, so the vine
//    is pinned where it attaches and swings most at the free end. The mesh has
//    to be UV'd with V running down its length for this to work.
//  - thetaOffset is world height times 30, which decorrelates the phase of
//    every vine in the level so they do not all swing in lockstep.
//
// Coordinate systems differ: Spark is Y-up, so wsPosition.y is height and the
// float3(x, 0, z) offset is horizontal. In s&box, Z is up, so height is
// wsPosition.z and the horizontal offset is float3(x, y, 0).
//
// prevWsOffset feeds Spark's velocity buffer for motion blur and TAA, which has
// no direct equivalent here, so it is dropped.
//=============================================================================

HEADER
{
	Description = "NS2 Unearthed Tree Vines";
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

	// Spark authors swayAmp in metres; s&box works in inches, so 0.25m.
	float g_flSwayAmp < UiGroup( ",0/,0/0" ); Default1( 9.8425 ); Range1( 0, 64 ); >;
	float g_flSwayFrequency < UiGroup( ",0/,0/0" ); Default1( 0.18 ); Range1( 0, 8 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput o = ProcessVertex( v );

		float3 vPositionWs = o.vPositionWs.xyz;

		const float flSwayStartV = 0.1;
		const float flSwayEndV = 0.9;
		const float flPi = 3.14159265359;

		float thetaOffset = vPositionWs.z * 30.0;
		float theta = g_flTime * g_flSwayFrequency * 2.0 * flPi + thetaOffset;

		float swayIntensity = saturate( ( o.vTextureCoords.y - flSwayStartV ) / ( flSwayEndV - flSwayStartV ) ) * g_flSwayAmp;

		o.vPositionWs.xyz += float3( sin( theta ), cos( theta ), 0.0 ) * swayIntensity;
		o.vPositionPs = Position3WsToPs( o.vPositionWs.xyz );

		return FinalizeVertex( o );
	}
}

PS
{
	#include "ns2/alpha_test.hlsl"
	#include "ns2/ns2.hlsl"

	RenderState( CullMode, NONE );

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap,   Srgb,   8, "None",             "_color",    ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap, Linear, 8, "None",             "_spec",     ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( EmissiveMap, Srgb,   8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( OpacityMap,  Linear, 8, "None",             "_opacity",  ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Srgb );   OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tOpacityMap  < Channel( RGBA, Box( OpacityMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flEmissiveMod < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		m.Opacity = Tex2DS( g_tOpacityMap, g_sWrap, uv ).r;

		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb;
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );

		Ns2ApplySpecularGloss( m, albedo, specularGloss.rgb, specularGloss.a );

		m.Emission = Tex2DS( g_tEmissiveMap, g_sWrap, uv ).rgb * g_flEmissiveMod;
		m.Normal = TransformNormal( Ns2DecodeNormal( Tex2DS( g_tNormalMap, g_sWrap, uv ) ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return Ns2ShadeAlphaTest( i, m, TextureDimensions2DS( g_tOpacityMap, 0 ), g_flAlphaTestReference );
	}
}
