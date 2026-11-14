HEADER
{
	Description = "GoldSrc world surface - RGB lightmaps with animated lightstyles";
}

FEATURES
{
	#include "common/features.hlsl"

	// GoldSrc rendermode on brush entities: kRenderTransTexture / kRenderTransColor need alpha
	// blending, kRenderGlow / kRenderTransAdd need additive. Flipped per material at load time
	// with Material.SetFeature. F_ADDITIVE_BLEND already comes from common/features.hlsl.
	Feature( F_TRANSLUCENT, 0..1, "Translucent" );
	FeatureRule( Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT ), "Requires translucency" );
}

MODES
{
	Forward();
	Depth(); 
}

COMMON
{
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_ADDITIVE_BLEND, F_ADDITIVE_BLEND, Sys( ALL ) );

	// Masked ( '{' ) textures are clipped explicitly in the pixel shader, so alpha to coverage is
	// only wanted on opaque surfaces - it would dither a translucent brush instead of blending it.
	#ifndef S_ALPHA_TEST
	#define S_ALPHA_TEST ( 1 - S_TRANSLUCENT )
	#endif

	#include "common/shared.hlsl"
	#include "procedural.hlsl"

	#define S_UV2 1
	#define CUSTOM_MATERIAL_INPUTS
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
	float4 vLightStyles : BLENDINDICES10 < Semantic( BlendIndices ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float3 vPositionOs : TEXCOORD14;
	float3 vNormalOs : TEXCOORD15;
	float4 vTangentUOs_flTangentVSign : TANGENT	< Semantic( TangentU_SignV ); >;
	float4 vColor : COLOR0;
	float4 vTintColor : COLOR1;
	float4 vLightStyle : TEXCOORD10;
};

VS
{
	#include "common/vertex.hlsl"

	Texture2D<float> g_tLightStyles < Attribute( "GoldSrcLightStyles" ); SrgbRead( false ); >;

	float GetLightstyleValue( int styleIndex )
	{
		return g_tLightStyles.Load( int3( styleIndex, 0, 0 ) );
	}

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vPositionOs = v.vPositionOs.xyz;
		i.vColor = v.vColor;

		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;

		i.vLightStyle[0] = GetLightstyleValue( v.vLightStyles[0] );
		i.vLightStyle[1] = GetLightstyleValue( v.vLightStyles[1] );
		i.vLightStyle[2] = GetLightstyleValue( v.vLightStyles[2] );
		i.vLightStyle[3] = GetLightstyleValue( v.vLightStyles[3] );

		VS_DecodeObjectSpaceNormalAndTangent( v, i.vNormalOs, i.vTangentUOs_flTangentVSign );

		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "common/classes/ScreenSpaceAmbientOcclusion.hlsl"
	#include "goldsrc_shading.hlsl"
	
	SamplerState g_sSampler0 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sSampler1 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	CreateInputTexture2D( Fullbright, Linear, 8, "None", "_mask", ",0/,0/0", Default4( 0.00, 0.00, 0.00, 0.00 ) );
	CreateInputTexture2D( LightmapR, Linear, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	CreateInputTexture2D( LightmapG, Linear, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	CreateInputTexture2D( LightmapB, Linear, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	Texture2D g_tFullbright < Channel( R, Box( Fullbright ), Linear ); OutputFormat( ATI1N ); SrgbRead( false ); >;
	Texture2D g_tLightmapR < Channel( RGBA, Box( LightmapR ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tLightmapG < Channel( RGBA, Box( LightmapG ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tLightmapB < Channel( RGBA, Box( LightmapB ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = float3( 1, 1, 1 );
		m.Normal = float3( 0, 0, 1 );
		m.Roughness = 1;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = 1;
		m.Emission = float3( 0, 0, 0 );
		m.Transmission = 0;
		
		float4 albedo = Tex2DS( g_tColor, g_sSampler0, i.vTextureCoords.xy );

		// Palette index 255 is punched out on '{' textures.
		clip( albedo.a - 0.5 );

		float2 l_1 = i.vTextureCoords.zw;

		float4 lmRed = Tex2DS( g_tLightmapR, g_sSampler1, l_1 );
		float4 lmGreen = Tex2DS( g_tLightmapG, g_sSampler1, l_1 );
		float4 lmBlue = Tex2DS( g_tLightmapB, g_sSampler1, l_1 );

		float4 lightStyle = i.vLightStyle;

		float3 style0 = float3( lmRed.x, lmGreen.x, lmBlue.x ) * lightStyle.x;
		float3 style1 = float3( lmRed.y, lmGreen.y, lmBlue.y ) * lightStyle.y;
		float3 style2 = float3( lmRed.z, lmGreen.z, lmBlue.z ) * lightStyle.z;
		float3 style3 = float3( lmRed.w, lmGreen.w, lmBlue.w ) * lightStyle.w;

		//
		// The lightmap bake writes the summed, scaled samples into an 8 bit texture, so anything
		// over 255 is clipped before it ever reaches the framebuffer.
		//
		float3 lightGamma = saturate( style0 + style1 + style2 + style3 );

		//
		// GoldSrc uploads world miptex through texgamma: T' = ( T / 255 ) ^ ( texgamma / gamma ),
		// which is ^0.8 at the default texgamma 2.0 / gamma 2.5. Our albedo was sRGB decoded on
		// sample, so encode it back to gamma space first.
		//
		float3 albedoGamma = pow( SrgbLinearToGamma( albedo.xyz ), 0.8 );
		m.Albedo = SrgbGammaToLinear( albedoGamma );
		m.Normal = TransformNormal( m.Normal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.GeometricNormal = i.vNormalWs;
		#if !S_MODE_DEPTH
			Decals::Apply( m.WorldPosition, m );
			albedoGamma = SrgbLinearToGamma( m.Albedo );
		#endif

		float fullbright = Tex2DS( g_tFullbright, g_sSampler0, i.vTextureCoords.xy ).r;

		//
		// R_BlendLightmaps composites the lightmap over the GL_REPLACE'd diffuse pass with
		// GL_DST_COLOR / GL_SRC_COLOR ( a 2x multiply ) and a 128/192 vertex colour, so the
		// overbright factor is exactly 2 * 2/3 = 4/3. There is no framebuffer gamma ramp in
		// GoldSrc, so this product _is_ the sRGB value on screen - decode it for our linear pipe.
		//
		float3 displayColor = saturate( lightGamma * ( 4.0 / 3.0 ) * albedoGamma );

		//
		// Brush entity rendermode. rendercolor/renderamt arrive as the per instance tint, and
		// GoldSrc applies them in the same gamma space the modulation happened in.
		//
		displayColor *= i.vTintColor.rgb;

		float3 finalColor = SrgbGammaToLinear( displayColor );
		float3 fullbrightColor = SrgbGammaToLinear( albedoGamma * i.vTintColor.rgb );

		#if ( S_TRANSLUCENT )
			m.Opacity = i.vTintColor.a;
		#else
			m.Opacity = 1;
		#endif
		m.Roughness = 1;
		m.Metalness = 0;

		float3 diffuseAO = float3( 1, 1, 1 );
		#if !S_TRANSLUCENT && !S_MODE_DEPTH
			LightingTerms_t aoLighting = InitLightingTerms();
			aoLighting.flDynamicAmbientOcclusion = ScreenSpaceAmbientOcclusion::Sample( i.vPositionSs );
			diffuseAO = CalculateDiffuseAmbientOcclusion( ShadingModelStandard::MaterialToCombinerInput( m ), aoLighting );
		#endif

		// '~' textures remain emissive, including filtered transitions in the mask.
		m.Emission = lerp( finalColor * diffuseAO, fullbrightColor, fullbright );

		//
		// The lightmap above is the surface's entire diffuse response, applied as emission, so
		// any indirect the scene adds on top is a second helping of light the surface already
		// has. Ambient probe volumes exist for dynamic models, which have nothing baked - the
		// world must opt out of them or every lit room reads brighter than GoldSrc ever did.
		//
		// AO is the lever for this rather than zeroing albedo: the shading model multiplies
		// indirect diffuse by it unconditionally, but only reaches direct diffuse through
		// flAmbientOcclusionDirectDiffuse, which is 0. So dynamic lights still land on walls
		// exactly as before, and only the ambient term is removed.
		//
		m.AmbientOcclusion = 0;

		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = i.vTextureCoords.xy;
		
		float4 shade = GoldSrcShade( i, m );
		
		return shade;
	}
}
