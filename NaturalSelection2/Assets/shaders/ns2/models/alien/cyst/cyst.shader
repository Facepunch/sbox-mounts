//=============================================================================
// Port of NS2 ns2/models/alien/cyst/cyst.surface_shader
//
//     <param name="alphaTest">true</param>
//
//     float paramSlow(float x, float p) { return 1 - pow(abs(x - 1), p); }
//
//     float f     = 1 - hiddenAmount;
//     float param = tex2D(blendMap, input.texCoord).a;
//     float textureSpeed = 2;
//     float destroySpeed = 2.5;
//
//     float4 specularGloss = tex2D(specularMap, input.texCoord);
//     material.tsNormal = DecodeNormal( tex2D(normalMap, input.texCoord) );
//     material.specular = specularGloss.rgb;
//     material.gloss    = specularGloss.a;
//
//     float opacity       = paramSlow(param, destroySpeed);
//     float maturityPulse = (1 + cos(time * 4)) * 2;
//
//     material.albedo   = tex2D(albedoMap, input.texCoord).rgb * connected
//                       + (1 - connected) * tex2D(unconnectedAlbedoMap, input.texCoord).rgb;
//     material.emissive = (tex2D(emissiveMap, input.texCoord).rgb * (connected + connected * maturity * maturityPulse)
//                       + (1 - connected) * tex2D(unconnectedEmissiveMap, input.texCoord).rgb) * f;
//
//     float killWarningPulse = (1 + cos(time * 12)) * 2;
//     material.emissive += float3((killWarning * 0.5) + 0.3 * killWarning * killWarningPulse, 0, 0);
//
//     material.opacity = f;
//     material.id = 1*highlight;
//
// Alien cysts. `connected` cross-fades between the healthy and the severed
// texture set, `maturity` adds a slow green pulse, and `killWarning` adds a
// fast red one.
//
// The NS2 source computes `opacity`, `param`, `textureSpeed` and
// `destroySpeed` and then never uses them - material.opacity is assigned plain
// `f`. Dropped, along with blendMap and overlayMap, which are only read by
// that dead code path. Their surface_shader defaults are consequently
// irrelevant here.
//
// The alpha test on `f` means a cyst is either fully drawn (hiddenAmount < 0.5)
// or fully clipped; there is no fade. That is the NS2 behaviour.
//
// Both emissive maps are srgb="true"; the albedo maps are not. The killWarning
// term is an authored constant and stays linear.
//
// material.id feeds Spark's alien vision pass; no s&box equivalent.
//=============================================================================

HEADER
{
	Description = "NS2 Cyst";
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

	CreateInputTexture2D( AlbedoMap,              Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( UnconnectedAlbedoMap,   Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,              Linear, 8, "NormalizeNormals", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap,            Linear, 8, "None",             "_spec",     ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( EmissiveMap,            Linear, 8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( UnconnectedEmissiveMap, Linear, 8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tAlbedoMap              < Channel( RGBA, Box( AlbedoMap ),              Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tUnconnectedAlbedoMap   < Channel( RGBA, Box( UnconnectedAlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap              < Channel( RGBA, Box( NormalMap ),              Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap            < Channel( RGBA, Box( SpecularMap ),            Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap            < Channel( RGBA, Box( EmissiveMap ),            Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tUnconnectedEmissiveMap < Channel( RGBA, Box( UnconnectedEmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flHiddenAmount < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flConnected    < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 1 ); >;
	float g_flMaturity     < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flKillWarning  < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;
	float g_flHighlight    < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		float f = 1.0 - g_flHiddenAmount;
		float connected = g_flConnected;

		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, uv );
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, uv );

		float maturityPulse = ( 1.0 + cos( g_flTime * 4.0 ) ) * 2.0;
		float killWarningPulse = ( 1.0 + cos( g_flTime * 12.0 ) ) * 2.0;

		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, uv ).rgb * connected
			+ ( 1.0 - connected ) * Tex2DS( g_tUnconnectedAlbedoMap, g_sWrap, uv ).rgb;

		float3 emissive = ( Ns2SrgbToLinear( Tex2DS( g_tEmissiveMap, g_sWrap, uv ).rgb ) * ( connected + connected * g_flMaturity * maturityPulse )
			+ ( 1.0 - connected ) * Ns2SrgbToLinear( Tex2DS( g_tUnconnectedEmissiveMap, g_sWrap, uv ).rgb ) ) * f;

		emissive += float3( ( g_flKillWarning * 0.5 ) + 0.3 * g_flKillWarning * killWarningPulse, 0.0, 0.0 );

		m.Opacity = f;
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
