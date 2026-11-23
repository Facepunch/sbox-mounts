//=============================================================================
// Port of NS2 ns2/shaders/PowerNode_emissive.surface_shader
//
//     <param name="alphaTest">true</param>
//
//     float paramQuick(float x, float p) { return pow(x, p); }
//     float paramSlow (float x, float p) { return 1 - pow(abs(x), p); }
//
//     float f            = 1 - hiddenAmount;
//     float param        = tex2D(blendMap, input.texCoord * 2.5).a;
//     float textureSpeed = 2;
//     float destroySpeed = 1;
//
//     float4 specularGloss = tex2D(specularMap, input.texCoord);
//     material.tsNormal = DecodeNormal( tex2D(normalMap, input.texCoord) );
//     material.specular = specularGloss.rgb;
//     material.gloss    = specularGloss.a;
//
//     float opacity = paramSlow(param, destroySpeed);
//     float blend   = paramQuick(param, textureSpeed);
//
//     if (blend + f > 1)
//     {
//         material.albedo   = tex2D(albedoMap, input.texCoord).rgb;
//         material.emissive = tex2D(emissiveMap, input.texCoord).rgb * emissiveAmount;
//     }
//     else
//     {
//         material.albedo   = tex2D(albedoMap, input.texCoord).rgb;
//         material.emissive = (1 - (blend + f)) * tex2D(overlayMap, input.texCoord * float2(10, 9)).rgb * float3(2, 3, 6) * 50;
//     }
//
//     material.opacity = opacity + f - 0.5;
//     if (material.opacity < 0.5) material.emissive = 0;
//     material.id = 1*highlight;
//
// The power node dissolve. blendMap.a is the spread parameter: as it rises the
// node fades out (opacity = 1 - param) while the circuit overlay flares blue
// through the transition band before the emissive map takes over.
//
// Notes on the port:
//
//  * paramSlow here is 1 - pow(abs(x), p), which is NOT the 1 - pow(abs(x-1),p)
//    used by marine / hive_chunks. Do not "fix" it.
//  * Both branches assign the same albedo, so it is hoisted out.
//  * The `if (opacity < 0.5) emissive = 0` guard is dead once alphaTest has
//    clipped everything below 0.5. Kept verbatim for fidelity; it costs
//    nothing and documents the original intent.
//  * emissiveMap is srgb="true" so it is decoded; blendMap and overlayMap are
//    not, so the overlay flare is written raw exactly as Spark writes it.
//  * blendMap and overlayMap have surface_shader defaults
//    (skulk_view_blood.dds / nanoshield_circuit.dds) which MaterialLoader
//    supplies for materials that do not override them.
//  * material.id feeds Spark's alien vision pass; no s&box equivalent.
//=============================================================================

HEADER
{
	Description = "NS2 Power Node Emissive";
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
	CreateInputTexture2D( BlendMap,    Linear, 8, "None",             "_mask",     ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( OverlayMap,  Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tBlendMap    < Channel( RGBA, Box( BlendMap ),    Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tOverlayMap  < Channel( RGBA, Box( OverlayMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flHiddenAmount   < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flEmissiveAmount < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 16 ); >;
	float g_flHighlight      < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		float f = 1.0 - g_flHiddenAmount;

		float param = Tex2DS( g_tBlendMap, g_sWrap, uv * 2.5 ).a;
		float opacity = 1.0 - abs( param );
		float blend = param * param;

		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );

		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb;
		float3 emissive;

		if ( blend + f > 1.0 )
		{
			emissive = Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, uv ).rgb ) * g_flEmissiveAmount;
		}
		else
		{
			emissive = ( 1.0 - ( blend + f ) ) * Tex2DS( g_tOverlayMap, g_sWrap, uv * float2( 10.0, 9.0 ) ).rgb * float3( 2.0, 3.0, 6.0 ) * 50.0;
		}

		m.Opacity = opacity + f - 0.5;
		clip( m.Opacity - 0.5 );
		if ( m.Opacity < 0.5 )
			emissive = 0.0;
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
