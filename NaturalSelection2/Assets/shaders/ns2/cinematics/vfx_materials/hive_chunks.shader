//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/hive_chunks.surface_shader
//
//     <param name="alphaTest">true</param>
//
//     float paramSlow(float x, float p) { return 1 - pow(abs(x - 1), p); }
//     const float destroySpeed = 1.5;
//     const float textureSpeed = 1;
//
//     float f = 1 - hiddenAmount;
//     float4 fireGlow  = tex2D(fireglowMap, input.texCoord);
//     float4 fireGlow2 = tex2D(fireglowMap, input.texCoord * float2(6, 1) + float2(time * 0.01, time * -0.05));
//     float  param   = tex2D(blendMap, input.texCoord).r;
//     float  opacity = paramSlow(param, 1.5);
//     float  blend   = pow(param, 1);
//
//     float4 specularGloss = tex2D(specularMap, input.texCoord);
//     material.tsNormal = DecodeNormal( tex2D(normalMap, input.texCoord) );
//     material.specular = specularGloss.rgb;
//     material.gloss    = specularGloss.a;
//
//     if (blend + f > 0.5)
//     {
//         material.albedo   = tex2D(albedoMap, input.texCoord).rgb + fireGlow.rgb * tint * fireGlow2.rgb * tint;
//         material.emissive = tex2D(emissiveMap, input.texCoord).rgb * f * glowIntensity;
//     }
//     else
//     {
//         material.albedo   = tex2D(overlayMap, input.texCoord * 4).rgb + fireGlow.rgb * tint * fireGlow2.rgb * tint;
//     }
//     material.opacity = opacity + f;
//
// Notes on the port:
//
//  * destroySpeed and textureSpeed are declared in the NS2 source and never
//    read. Dropped.
//  * The else branch never assigns emissive, so it keeps the zero the Material
//    struct is initialised with. Made explicit here.
//  * emissiveMap and fireglowMap are srgb="true"; albedoMap, blendMap and
//    overlayMap are not. We mirror exactly what Spark's samplers do: the two
//    fireglow samples are decoded, multiplied together with tint, and the
//    result is added to the *raw* albedo sample. Spark's albedo target holds
//    gamma values, so that sum is then decoded once inside
//    Ns2ApplySpecularGloss. Mixed, but faithful - tint stays raw as in NS2.
//  * overlayMap defaults to models/misc/infestation/infestation.dds and
//    blendMap's default element is empty; both are supplied by the
//    surface_shader default pass in MaterialLoader.
//=============================================================================

HEADER
{
	Description = "NS2 Hive Chunks";
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
	float4 vTintColor : COLOR1;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;

		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;

		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap,    Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,    Linear, 8, "NormalizeNormals", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap,  Linear, 8, "None",             "_spec",     ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( EmissiveMap,  Linear, 8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( FireglowMap,  Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( BlendMap,     Linear, 8, "None",             "_mask",     ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( OverlayMap,   Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tFireglowMap < Channel( RGBA, Box( FireglowMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tBlendMap    < Channel( RGBA, Box( BlendMap ),    Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tOverlayMap  < Channel( RGBA, Box( OverlayMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float  g_flHiddenAmount  < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float  g_flGlowIntensity < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;
	float4 g_vTint           < UiGroup( ",0/,0/0" ); Default4( 1.0, 1.0, 1.0, 1.0 ); >;

	float Ns2ParamSlow( float x, float p )
	{
		return 1.0 - pow( abs( x - 1.0 ), p );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		float f = 1.0 - g_flHiddenAmount;

		float3 fireGlow  = Ns2SrgbToLinear( Tex2DS( g_tFireglowMap, g_sWrap, uv ).rgb );
		float3 fireGlow2 = Ns2SrgbToLinear( Tex2DS( g_tFireglowMap, g_sWrap, uv * float2( 6.0, 1.0 ) + float2( g_flTime * 0.01, g_flTime * -0.05 ) ).rgb );
		float3 fire = fireGlow * g_vTint.rgb * fireGlow2 * g_vTint.rgb;

		float param   = Tex2DS( g_tBlendMap, g_sWrap, uv ).r;
		float opacity = Ns2ParamSlow( param, 1.5 );
		float blend   = param;

		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );

		float3 albedo;
		float3 emissive;

		if ( blend + f > 0.5 )
		{
			albedo   = Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb + fire;
			emissive = Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, uv ).rgb ) * f * g_flGlowIntensity;
		}
		else
		{
			albedo   = Tex2DS( g_tOverlayMap, g_sWrap, uv * 4.0 ).rgb + fire;
			emissive = 0.0;
		}

		m.Opacity = opacity + f;
		clip( m.Opacity - 0.5 );
		m.Opacity = 1.0;

		Ns2ApplySpecularGloss( m, albedo * i.vTintColor.rgb, specularGloss.rgb, specularGloss.a );
		m.Emission = emissive;

		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = TransformNormal( Ns2DecodeNormal( normal ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
