//=============================================================================
// Port of NS2 ns2/cinematics/vfx_materials/decals/alien_blood.surface_shader
//
//     <param name="alphaTest">true</param>
//     <param name="vsNormal">true</param>
//     <param name="osPosition">true</param>
//
//     const float3 tint = float3(4, 20, 1);
//     float intensity = 1.0;
//     if (endTime != 0) intensity = clamp( (endTime - time)/1, 0, 1);
//
//     float2 texCoord = input.texCoord;
//     if (scale != 0)
//     {
//         float smoothEdge = abs(input.osPosition.y / scale) * 0.5;
//         texCoord.x = clamp(texCoord.x + smoothEdge, -1, 1);
//         texCoord.y = clamp(texCoord.y - smoothEdge, -1, 1);
//         float distance = length(input.osPosition);
//         intensity *= clamp(1 - (distance / (scale * 0.5)) * (abs(input.osPosition.y) / (scale * 0.5)), 0, 1);
//     }
//
//     float4 specularGloss = tex2D(specularMap, texCoord);
//     float4 glowPoints  = tex2D(emissiveMap, texCoord*6);
//     float4 glowPoints2 = tex2D(emissiveMap, texCoord*2 + float2(time * .01, time * 0.01));
//     material.albedo   = tex2D(albedoMap, texCoord).rgb;
//     material.tsNormal = DecodeNormal( tex2D(normalMap, texCoord) );
//     material.specular = specularGloss.rgb * intensity;
//     material.gloss    = specularGloss.a * intensity;
//     material.emissive = glowPoints.rgb * glowPoints2.rgb * tint;
//     material.opacity  = tex2D(opacityMap, texCoord).r * intensity;
//
// A splat decal that shears its UVs and fades towards the edge of its own
// object-space bounds, so a single mesh can be scaled to any splat size.
//
// osPosition is in NS2 metres and `scale` is authored in metres, so the
// incoming s&box object-space position is divided by NS2_UNITS_PER_METRE
// before either is used (same as exominigunview).
//
// endTime is a game-driven absolute timestamp for the decal's fade-out. Nothing
// on the s&box side sets it, so with the default 0 the branch is skipped and
// the decal stays at full intensity - which is what we want for static props.
//
// emissiveMap is NOT srgb here, so Spark writes the raw texel product into its
// linear lighting buffer. We reproduce that byte-for-byte rather than
// "correcting" it - the two glow samples are multiplied in gamma space and the
// result goes straight to Emission.
//=============================================================================

HEADER
{
	Description = "NS2 Alien Blood Decal";
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
	float3 vPositionOs : TEXCOORD14;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		i.vPositionOs = v.vPositionOs.xyz;

		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;

		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "ns2/ns2.hlsl"

	static const float NS2_UNITS_PER_METRE = 40.0;

	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	CreateInputTexture2D( AlbedoMap,   Linear, 8, "None",             "_color",    ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( NormalMap,   Linear, 8, "NormalizeNormals", "_normal",   ",0/,0/0", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SpecularMap, Linear, 8, "None",             "_spec",     ",0/,0/0", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( OpacityMap,  Linear, 8, "None",             "_opacity",  ",0/,0/0", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( EmissiveMap, Linear, 8, "None",             "_emissive", ",0/,0/0", Default3( 0.0, 0.0, 0.0 ) );

	Texture2D g_tAlbedoMap   < Channel( RGBA, Box( AlbedoMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tNormalMap   < Channel( RGBA, Box( NormalMap ),   Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tSpecularMap < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tOpacityMap  < Channel( RGBA, Box( OpacityMap ),  Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float g_flEndTime < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 100000 ); >;
	float g_flScale   < UiGroup( ",0/,0/0" ); Default1( 0.0 ); Range1( 0, 100 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		const float3 tint = float3( 4.0, 20.0, 1.0 );

		float3 osPosition = i.vPositionOs / NS2_UNITS_PER_METRE;

		float intensity = 1.0;
		if ( g_flEndTime != 0.0 )
			intensity = saturate( g_flEndTime - g_flTime );

		float2 texCoord = i.vTextureCoords.xy;

		if ( g_flScale != 0.0 )
		{
			float smoothEdge = abs( osPosition.y / g_flScale ) * 0.5;
			texCoord.x = clamp( texCoord.x + smoothEdge, -1.0, 1.0 );
			texCoord.y = clamp( texCoord.y - smoothEdge, -1.0, 1.0 );

			float dist = length( osPosition );
			intensity *= saturate( 1.0 - ( dist / ( g_flScale * 0.5 ) ) * ( abs( osPosition.y ) / ( g_flScale * 0.5 ) ) );
		}

		float3 albedo = Tex2DS( g_tAlbedoMap, g_sWrap, texCoord ).rgb;
		float4 normal = Tex2DS( g_tNormalMap, g_sWrap, texCoord );
		float4 specularGloss = Tex2DS( g_tSpecularMap, g_sWrap, texCoord );

		float3 glowPoints  = Tex2DS( g_tEmissiveMap, g_sWrap, texCoord * 6.0 ).rgb;
		float3 glowPoints2 = Tex2DS( g_tEmissiveMap, g_sWrap, texCoord * 2.0 + float2( g_flTime * 0.01, g_flTime * 0.01 ) ).rgb;

		m.Opacity = Tex2DS( g_tOpacityMap, g_sWrap, texCoord ).r * intensity;
		clip( m.Opacity - 0.5 );
		m.Opacity = 1.0;

		Ns2ApplySpecularGloss( m, albedo * i.vTintColor.rgb, specularGloss.rgb * intensity, specularGloss.a * intensity );
		m.Emission = glowPoints * glowPoints2 * tint;

		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Normal = TransformNormal( Ns2DecodeNormal( normal ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = texCoord;

		return ShadingModelStandard::Shade( i, m );
	}
}
