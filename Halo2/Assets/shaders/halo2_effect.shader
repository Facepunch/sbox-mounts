HEADER
{
	Description = "Halo 2 character overlay passes";
}
FEATURES
{
	#include "common/features.hlsl"
	Feature( F_PASS, 0..11( 0="None", 1="Emblem", 2="Plasma", 3="Hair", 4="Additive", 5="Eyes", 6="Self illumination", 7="Bloom", 8="Masked detail", 9="Meter", 10="Clamped illumination", 11="Ammunition digit" ), "Halo 2 pass" );
}
MODES
{
	Forward();
}
COMMON
{
	#include "common/shared.hlsl"
	#define PASS_EMBLEM 1
	#define PASS_PLASMA 2
	#define PASS_HAIR 3
	#define PASS_ADDITIVE 4
	#define PASS_EYES 5
	#define PASS_SELF_ILLUMINATION 6
	#define PASS_BLOOM 7
	#define PASS_MASKED_DETAIL 8
	#define PASS_METER 9
	#define PASS_CLAMPED_ILLUMINATION 10
	#define PASS_AMMO_DIGIT 11
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
	PixelInput MainVs( VertexInput v ) { return FinalizeVertex( ProcessVertex( v ) ); }
}
PS
{
	StaticCombo( S_PASS, F_PASS, Sys( ALL ) );
	#include "common/pixel.hlsl"
	BoolAttribute( translucent, true );
	RenderState( BlendEnable, true );
	RenderState( DepthWriteEnable, false );
	RenderState( SrcBlend, ONE );
	#if S_PASS == PASS_PLASMA || S_PASS == PASS_ADDITIVE || S_PASS == PASS_AMMO_DIGIT || S_PASS == PASS_SELF_ILLUMINATION || S_PASS == PASS_CLAMPED_ILLUMINATION || S_PASS == PASS_BLOOM
		RenderState( DstBlend, ONE );
	#else
		RenderState( DstBlend, INV_SRC_ALPHA );
	#endif
	#if S_PASS == PASS_PLASMA
		RenderState( CullMode, NONE );
	#endif
	CreateInputTexture2D( Layer0, Linear, 8, "None", "", "", Default4( 0,0,0,0 ) );
	CreateInputTexture2D( Layer1, Linear, 8, "None", "", "", Default4( 0,0,0,0 ) );
	CreateInputTexture2D( Layer2, Linear, 8, "None", "", "", Default4( 0,0,0,0 ) );
	CreateInputTexture2D( Layer3, Linear, 8, "None", "", "", Default4( 0,0,0,0 ) );
	Texture2D g_tLayer0 < Channel( RGBA, Box( Layer0 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tLayer1 < Channel( RGBA, Box( Layer1 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tLayer2 < Channel( RGBA, Box( Layer2 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tLayer3 < Channel( RGBA, Box( Layer3 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	CreateInputTextureCube( Reflection, Srgb, 8, "", "", "", Default3( 0,0,0 ) );
	CreateTextureCube( g_tReflection ) < Channel( RGB, GGXCubeMapBlur( Reflection ), Srgb ); OutputFormat( DXT1 ); SrgbRead( true ); >;
	float4 g_vRow0 < Default4( 1,0,0,0 ); >;
	float4 g_vRow1 < Default4( 0,1,0,0 ); >;
	float4 g_vRow2 < Default4( 1,0,0,0 ); >;
	float4 g_vRow3 < Default4( 0,1,0,0 ); >;
	float4 g_vRow4 < Default4( 1,0,0,0 ); >;
	float4 g_vRow5 < Default4( 0,1,0,0 ); >;
	float4 g_vRow6 < Default4( 1,0,0,0 ); >;
	float4 g_vRow7 < Default4( 0,1,0,0 ); >;
	float4 g_vPlasmaScroll < Default4( 0,0,0,0 ); >;
	float4 g_vConstant0 < Default4( 0,0,0,0 ); >;
	float4 g_vConstant1 < Default4( 0,0,0,0 ); >;
	float4 g_vConstant2 < Default4( 0,0,0,0 ); >;
	float4 g_vConstant3 < Default4( 0,0,0,0 ); >;
	float4 g_vConstant4 < Default4( 0,0,0,0 ); >;
	#if S_PASS == PASS_EMBLEM
		float3 g_vTertiaryColor < Default3( 1,1,1 ); >;
		float3 g_vQuaternaryColor < Default3( 1,1,1 ); >;
	#elif S_PASS == PASS_HAIR
		float3 g_vPrimaryColor < Default3( 1,1,1 ); >;
		float3 g_vSecondaryColor < Default3( 1,1,1 ); >;
	#endif

	#if S_PASS == PASS_CLAMPED_ILLUMINATION
		SamplerState IllumSampler < Filter( MIN_MAG_MIP_LINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	#endif

	#if S_PASS == PASS_METER
		float WeaponMeter < Attribute( "WeaponMeter" ); Default( 0 ); >;
		float2 g_vMeterRange < Default2( 0, 1 ); >;
	#endif

	#if S_PASS == PASS_AMMO_DIGIT
		int WeaponRounds < Attribute( "WeaponRounds" ); Default( 0 ); >;
		int g_nDigitDivisor < Default( 1 ); >;
	#endif

	float4 ShadeOverlay( PixelInput i )
	{
		float4 vertexUv = float4( i.vTextureCoords.xy, 0, 1 );
		float2 uv0 = float2( dot( vertexUv, g_vRow0 ), dot( vertexUv, g_vRow1 ) );
		float2 uv1 = float2( dot( vertexUv, g_vRow2 ), dot( vertexUv, g_vRow3 ) );
		float2 uv2 = float2( dot( vertexUv, g_vRow4 ), dot( vertexUv, g_vRow5 ) );
		float2 uv3 = float2( dot( vertexUv, g_vRow6 ), dot( vertexUv, g_vRow7 ) );
		#if S_PASS == PASS_PLASMA
			// plasma_mask_offset: blend the scrolling alpha fields, then shape their difference.
			float a = g_tLayer0.Sample( g_sAniso, uv0 + g_vPlasmaScroll.xy * g_flTime ).a;
			float b = g_tLayer1.Sample( g_sAniso, uv1 + g_vPlasmaScroll.zw * g_flTime ).a;
			float offset = g_tLayer2.Sample( g_sAniso, uv2 ).a;
			float4 mask = g_tLayer3.Sample( g_sAniso, uv3 );
			a = lerp( offset, a, g_vConstant1.a );
			b = lerp( 0.5, b, g_vConstant0.b );
			float delta = (a - b) * (1 - mask.a);
			float high = saturate( 128.0 / 255 + delta );
			float low = saturate( 128.0 / 255 - delta );
			float edge = 4 * lerp( low * low, high * high, 128.0 / 255 - delta );
			float curve = 2 * saturate( edge ) - 1;
			edge = saturate( edge * curve * curve );
			edge *= 2 - edge;
			return float4( saturate( mask.rgb * g_vConstant2.rgb * edge + (1 - mask.a) * g_vConstant3.rgb ) * g_vConstant4.a, 0 );
		#elif S_PASS == PASS_AMMO_DIGIT
			int digit = (max( WeaponRounds, 0 ) / max( g_nDigitDivisor, 1 )) % 10;
			float2 pageUv = float2( uv0.x, (frac( uv0.y ) + digit) * 0.1 );
			float3 glyph = g_tLayer0.SampleGrad( g_sAniso, pageUv, ddx( uv0 ) * float2( 1, 0.1 ), ddy( uv0 ) * float2( 1, 0.1 ) ).rgb;
			return float4( glyph * g_vConstant0.rgb, 1 );
		#elif S_PASS == PASS_MASKED_DETAIL
			float4 base = g_tLayer0.Sample( g_sAniso, uv0 );
			float3 detail = g_tLayer1.Sample( g_sAniso, uv1 ).rgb;
			float3 add = g_tLayer2.Sample( g_sAniso, uv2 ).rgb;
			float3 mask = g_tLayer3.Sample( g_sAniso, uv3 ).rgb;
			float3 color = saturate( saturate( 2 * detail * base.rgb ) + add * base.a );
			return float4( color * lerp( 1.0.xxx, mask, base.a ) * base.a * g_vConstant0.a, base.a );
		#elif S_PASS == PASS_EYES
			Material m = Material::Init( i );
			float3 dir = reflect( -CalculatePositionToCameraDirWs( m.WorldPosition ), normalize( i.vNormalWs ) );
			float3 reflection = TexCube( g_tReflection, dir ).rgb * g_vConstant0.rgb;
			float3 mask = g_tLayer1.Sample( g_sAniso, uv1 ).rgb;
			float3 base = g_tLayer2.Sample( g_sAniso, uv2 ).rgb * g_vConstant3.rgb;
			float4 overlay = g_tLayer3.Sample( g_sAniso, uv3 );
			float alpha = overlay.a * g_vConstant2.a;
			return float4( saturate( base + reflection * mask + overlay.rgb * g_vConstant1.rgb * alpha ), alpha );
		#else
			#if S_PASS == PASS_CLAMPED_ILLUMINATION
				float4 base = g_tLayer0.Sample( IllumSampler, uv0 );
			#else
				float4 base = g_tLayer0.Sample( g_sAniso, uv0 );
			#endif
			#if S_PASS == PASS_HAIR
				float2 mask = g_tLayer1.Sample( g_sAniso, uv1 ).rg;
				Material m = Material::Init( i );
				m.Albedo = base.rgb * lerp( 1.0.xxx, g_vPrimaryColor, mask.r ) * lerp( 1.0.xxx, g_vSecondaryColor, mask.g );
				return float4( ShadingModelStandard::Shade( m ).rgb * base.a, base.a );
			#elif S_PASS == PASS_SELF_ILLUMINATION || S_PASS == PASS_CLAMPED_ILLUMINATION || S_PASS == PASS_BLOOM
				#if S_PASS == PASS_BLOOM
					base.rgb = saturate( base.rgb + g_tLayer1.Sample( g_sAniso, uv1 ).rgb ) * g_tLayer2.Sample( g_sAniso, uv2 ).rgb;
				#endif
				float3 glow = saturate( base.rgb * g_vConstant0.rgb );
				float power = saturate( dot( glow, glow ) );
				if ( power == 0 ) discard;
				return float4( glow, power );
			#elif S_PASS == PASS_METER
				// The retail combiner selects a tint and opacity using the display texture's alpha.
				bool high = base.a >= lerp( g_vMeterRange.x, g_vMeterRange.y, saturate( WeaponMeter ) );
				float4 response = g_tLayer2.SampleLevel( g_sAniso, float2( (saturate( WeaponMeter ) * 255 + 0.5) / 256, 0.5 ), 0 );
				float3 tint = high ? g_vConstant3.rgb : response.rgb;
				float opacity = (high ? response.a : g_vConstant2.a) * g_tLayer1.Sample( g_sAniso, uv1 ).a;
				return float4( base.rgb * tint * opacity, opacity );
			#elif S_PASS == PASS_ADDITIVE
				return float4( base.rgb * g_vConstant0.rgb, base.a );
			#elif S_PASS == PASS_EMBLEM
				float alpha = base.g + (1 - base.g) * base.a;
				float3 color = base.r * g_vTertiaryColor + (1 - base.g) * base.b * g_vQuaternaryColor;
				return float4( saturate( color ) * alpha, alpha );
			#else
				return 0;
			#endif
		#endif
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 overlay = ShadeOverlay( i );
		return float4( overlay.rgb * i.vVertexColor.rgb, overlay.a ) * i.vVertexColor.a;
	}
}
