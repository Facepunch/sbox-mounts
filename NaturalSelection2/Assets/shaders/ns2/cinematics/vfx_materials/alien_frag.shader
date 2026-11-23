//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/alien_frag.surface_shader
//
//     <param name="alphaTest">true</param>
//
//     float paramQuick(float x, float p) { return pow(x, p); }
//     float paramSlow (float x, float p) { return 1 - pow(abs(x - 1), p); }
//
//     float fadout = input.shaderParam.x;
//     float f      = 1 - fadout;
//     float param  = tex2D(blendMap, input.texCoord).a;
//     float textureSpeed = 2;
//     float destroySpeed = 2.5;
//     const float3 tint = float3(.2, 90, 50);
//     float3 fireGlow  = tex2D(fireglowMap, input.texCoord*3).rgb * tint;
//     float3 fireGlow2 = tex2D(fireglowMap, input.texCoord*8 + float2(time * -1, time * 1)).rgb * tint;
//
//     float4 specularGloss = tex2D(specularMap, input.texCoord);
//     material.tsNormal = DecodeNormal( tex2D(normalMap, input.texCoord) );
//     material.specular = specularGloss.rgb;
//     material.gloss    = specularGloss.a;
//
//     float opacity = paramSlow(param, destroySpeed);
//     float blend   = paramQuick(param, textureSpeed);
//
//     if (blend + f > .4) material.albedo = tex2D(albedoMap, input.texCoord).rgb + (fireGlow*fireGlow2);
//     else                material.albedo = tex2D(overlayMap, input.texCoord * 4).rgb + (fireGlow*fireGlow2);
//
//     material.opacity  = opacity + f;
//     material.emissive = tex2D(emissiveMap, input.texCoord).rgb * f * glowIntensity;
//
// The gib/chunk dissolve for alien structures. Same family as hive_chunks, but
// the exponents differ (destroySpeed 2.5 vs 1.5, textureSpeed 2 vs 1), the
// blend threshold is 0.4 rather than 0.5, the spread parameter comes from
// blendMap.a rather than .r, and the emissive is written unconditionally.
//
// `fadout` is Spark's per-instance shaderParam.x, driven by the cinematic
// system. s&box has no equivalent, so it is exposed as a normal float input
// defaulting to 0 (fully present).
//
// emissiveMap and fireglowMap are srgb="true" so their samples are decoded;
// albedoMap, blendMap and overlayMap are not. The decoded fireglow product is
// added to the raw albedo sample - Spark's albedo target is gamma, so the sum
// is decoded once inside Ns2ApplySpecularGloss. Faithful, if inconsistent.
//
// tint is enormous (.2, 90, 50), so the fireglow saturates to a hard green
// wherever the map is non-black. That is intended.
//=============================================================================

HEADER
{
	Description = "NS2 Alien Frag";
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

	CreateInputTexture2D( AlbedoMap,   Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap, Linear, 8, "None",             "_spec",     ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( FireglowMap, Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( BlendMap,    Linear, 8, "None",             "_mask",     ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( OverlayMap,  Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tFireglowMap < Channel( RGBA, Box( FireglowMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tBlendMap    < Channel( RGBA, Box( BlendMap ),    Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tOverlayMap  < Channel( RGBA, Box( OverlayMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flHiddenAmount  < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flGlowIntensity < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		const float3 tint = float3( 0.2, 90.0, 50.0 );

		float f = 1.0 - g_flHiddenAmount;

		float3 fireGlow  = Ns2SrgbToLinear( Tex2DS( g_tFireglowMap, g_sWrap, uv * 3.0 ).rgb ) * tint;
		float3 fireGlow2 = Ns2SrgbToLinear( Tex2DS( g_tFireglowMap, g_sWrap, uv * 8.0 + float2( g_flTime * -1.0, g_flTime * 1.0 ) ).rgb ) * tint;
		float3 fire = fireGlow * fireGlow2;

		float param = Tex2DS( g_tBlendMap, g_sWrap, uv ).a;
		float opacity = 1.0 - pow( abs( param - 1.0 ), 2.5 );
		float blend = param * param;

		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );

		float3 albedo;
		if ( blend + f > 0.4 )
			albedo = Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb + fire;
		else
			albedo = Tex2DS( g_tOverlayMap, g_sWrap, uv * 4.0 ).rgb + fire;

		m.Opacity = opacity + f;
		clip( m.Opacity - 0.5 );
		m.Opacity = 1.0;

		Ns2ApplySpecularGloss( m, albedo * i.vTintColor.rgb, specularGloss.rgb, specularGloss.a );
		m.Emission = Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, uv ).rgb ) * f * g_flGlowIntensity;

		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = TransformNormal( Ns2DecodeNormal( normal ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
