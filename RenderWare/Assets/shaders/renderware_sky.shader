
HEADER
{
	Description = "Renderware sky dome: timecyc gradient plus GTA's own sky sprites (clouds, sun, moon, stars). Drawn on a camera-centred dome behind everything.";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward(); // no depth or shadow pass - it's infinitely far away
}

COMMON
{
	#include "common/shared.hlsl"
	#include "sky.fxc"

	#define CUSTOM_MATERIAL_INPUTS
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float4 vPositionOs : POSITION < Semantic( PosXyz ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float3 vSkyLookupInterpolant : TEXCOORD0;
	float3 vPositionWs : TEXCOORD1;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs	: SV_Position;
	#endif
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
	#define IS_SPRITECARD 1// To avoid compile errors with missing VS_INPUT
	#include "vr_lighting.fxc"

	// Constants ----------------------------------------------------------------------------------------------------------------------------------------------

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
	PS_INPUT MainVs( const VS_INPUT i )
	{
		PS_INPUT o;

		o.vPositionWs = i.vPositionOs.xyz;

		o.vSkyLookupInterpolant.xyz = ComputeSkyLookupInterpolant( i.vPositionOs.xyz, false );

		float flSkyboxScale = g_flNearPlane + g_flFarPlane;
		float3 vPositionWs = g_vCameraPositionWs.xyz + i.vPositionOs.xyz * flSkyboxScale;
		
		o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );
		o.vPositionWs.xyz = vPositionWs;

		return o;
	}
}

PS
{
	#define BLEND_MODE_ALREADY_SET 1
	#define DEPTH_STATE_ALREADY_SET 1
	#include "common/pixel.hlsl"

	BoolAttribute( sky, true );

	RenderState( BlendEnable, false );
	RenderState( CullMode, NONE );
	RenderState( DepthEnable, true );
	RenderState( DepthWriteEnable, false );
	RenderState( DepthFunc, GREATER_EQUAL ); // reversed Z: only where nothing nearer was drawn, like the stock sky shader

	SamplerState g_sClamp < Filter( Bilinear ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	// GTA's sky sprites from particle.txd, bound by WorldLoader
	CreateInputTexture2D( Cloud1, Srgb, 8, "None", "_color", "Sprites,0/0", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( Cloud2, Srgb, 8, "None", "_color", "Sprites,0/1", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( Cloud3, Srgb, 8, "None", "_color", "Sprites,0/2", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CloudMasked, Srgb, 8, "None", "_color", "Sprites,0/3", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( CloudHilit, Srgb, 8, "None", "_color", "Sprites,0/4", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( Corona, Srgb, 8, "None", "_color", "Sprites,0/5", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( Star, Srgb, 8, "None", "_color", "Sprites,0/6", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( Moon, Srgb, 8, "None", "_color", "Sprites,0/7", Default4( 1, 1, 1, 1 ) );

	// SrgbRead( false ): everything here is blended in gamma space like the game, converted once at the end
	Texture2D g_tCloud1 < Channel( RGBA, Box( Cloud1 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tCloud2 < Channel( RGBA, Box( Cloud2 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tCloud3 < Channel( RGBA, Box( Cloud3 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tCloudMasked < Channel( RGBA, Box( CloudMasked ), Srgb ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tCloudHilit < Channel( RGBA, Box( CloudHilit ), Srgb ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tCorona < Channel( RGBA, Box( Corona ), Srgb ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tStar < Channel( RGBA, Box( Star ), Srgb ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tMoon < Channel( RGBA, Box( Moon ), Srgb ); OutputFormat( DXT5 ); SrgbRead( false ); >;

	// set by WorldLoader from the time cycle; colours are gamma space like the game's
	float3 g_vSkyTop < UiType( Color ); Default3( 0.35, 0.78, 1.0 ); UiGroup( "Sky" ); >;
	float3 g_vSkyBottom < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Sky" ); >;
	float g_flGradientHeight < Default( 0.35 ); Range( 0.01, 1.0 ); UiGroup( "Sky" ); >;

	float3 g_vSunDirection < Default3( 0.0, 0.7, 0.7 ); UiGroup( "Sun" ); >;
	float3 g_vSunCore < UiType( Color ); Default3( 1.0, 0.5, 0.0 ); UiGroup( "Sun" ); >;
	float3 g_vSunCorona < UiType( Color ); Default3( 1.0, 0.5, 0.0 ); UiGroup( "Sun" ); >;
	float g_flSunSize < Default( 1.0 ); Range( 0.0, 10.0 ); UiGroup( "Sun" ); >;

	float g_flNight < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Night" ); >; // star / moon visibility

	float3 g_vLowClouds < UiType( Color ); Default3( 0.5, 0.5, 0.5 ); UiGroup( "Clouds" ); >;
	float g_flLowCloudAlpha < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Clouds" ); >;
	float3 g_vCloudTop < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Clouds" ); >;
	float3 g_vCloudBottom < UiType( Color ); Default3( 0.5, 0.5, 0.5 ); UiGroup( "Clouds" ); >;
	int g_nFluffyClouds < Default( 8 ); Range( 0, 32 ); UiGroup( "Clouds" ); >;

	float Hash( float n ) { return frac( sin( n * 12.9898 + 4.1414 ) * 43758.5453 ); }

	float3 DirectionFrom( float azimuth, float elevation )
	{
		return float3( cos( azimuth ) * cos( elevation ), sin( azimuth ) * cos( elevation ), sin( elevation ) );
	}

	// where 'dir' lands on a camera facing sprite centred on 'centre' with angular half size 'halfSize' (radians),
	// as 0-1 uvs; false if it misses
	bool SpriteUv( float3 dir, float3 centre, float2 halfSize, out float2 uv )
	{
		uv = 0;
		float facing = dot( dir, centre );
		if ( facing <= 0 )
			return false;

		float3 right = normalize( cross( float3( 0, 0, 1 ), centre ) );
		float3 up = cross( centre, right );

		float2 local = float2( dot( dir, right ), dot( dir, up ) ) / facing / halfSize;
		if ( any( abs( local ) >= 1 ) )
			return false;

		uv = local * float2( 0.5, -0.5 ) + 0.5;
		return true;
	}

	float4 MainPs( PS_INPUT i ) : SV_Target0
	{
		float3 dir = normalize( i.vSkyLookupInterpolant.xyz );

		//
		// gradient: sky bottom at and below the horizon (also the fog colour), up to sky top
		//
		float t = saturate( dir.z / g_flGradientHeight );
		t = t * t * ( 3.0 - 2.0 * t );
		float3 color = lerp( g_vSkyBottom, g_vSkyTop, t );

		float2 uv;

		//
		// stars and moon (additive), night only
		//
		if ( g_flNight > 0 )
		{
			[loop]
			for ( int s = 0; s < 11; s++ )
			{
				float3 centre = DirectionFrom( Hash( s ) * 6.2832, 0.15 + Hash( s + 20 ) * 0.9 );
				float size = 0.008 + Hash( s + 40 ) * 0.008;
				if ( SpriteUv( dir, centre, size, uv ) )
				{
					float4 tex = g_tStar.SampleLevel( g_sClamp, uv, 0 );
					color += tex.rgb * tex.a * g_flNight;
				}
			}

			// opposite the sun
			if ( SpriteUv( dir, -g_vSunDirection, 0.06, uv ) )
			{
				float4 tex = g_tMoon.SampleLevel( g_sClamp, uv, 0 );
				color += tex.rgb * tex.a * g_flNight;
			}
		}

		//
		// sun: the corona sprite twice, a small core and a wide glow, in the timecyc colours (additive)
		//
		float sunAbove = saturate( ( g_vSunDirection.z + 0.05 ) * 10.0 );
		if ( sunAbove > 0 )
		{
			if ( SpriteUv( dir, g_vSunDirection, 0.05 * g_flSunSize, uv ) )
			{
				float4 tex = g_tCorona.SampleLevel( g_sClamp, uv, 0 );
				color += g_vSunCorona * tex.rgb * tex.a * 0.6 * sunAbove;
			}

			if ( SpriteUv( dir, g_vSunDirection, 0.02 * g_flSunSize, uv ) )
			{
				float4 tex = g_tCorona.SampleLevel( g_sClamp, uv, 0 );
				color += g_vSunCore * tex.rgb * tex.a * sunAbove;
			}
		}

		//
		// low clouds: a ring of long thin sprites sitting on the horizon (alpha blended)
		//
		if ( g_flLowCloudAlpha > 0 && dir.z > -0.05 && dir.z < 0.25 )
		{
			[loop]
			for ( int c = 0; c < 12; c++ )
			{
				float3 centre = DirectionFrom( ( c + Hash( c + 60 ) * 0.6 ) * ( 6.2832 / 12.0 ), 0.02 + Hash( c + 80 ) * 0.05 );
				if ( !SpriteUv( dir, centre, float2( 0.4, 0.05 ), uv ) )
					continue;

				int which = c % 3;
				float4 tex = which == 0 ? g_tCloud1.SampleLevel( g_sClamp, uv, 0 )
					: which == 1 ? g_tCloud2.SampleLevel( g_sClamp, uv, 0 )
					: g_tCloud3.SampleLevel( g_sClamp, uv, 0 );

				// these sprites carry the cloud in their colour with solid alpha - black means empty - so the
				// brightness is the mask, and the cloud takes the timecyc low cloud colour
				float mask = dot( tex.rgb, float3( 0.299, 0.587, 0.114 ) ) * tex.a;
				color = lerp( color, g_vLowClouds, mask * g_flLowCloudAlpha );
			}
		}

		//
		// fluffy clouds: big masked sprites, bottom colour below fading to top colour above, lit up
		// with the highlight sprite towards the sun
		//
		if ( dir.z > 0 )
		{
			[loop]
			for ( int f = 0; f < g_nFluffyClouds; f++ )
			{
				float3 centre = DirectionFrom( Hash( f + 100 ) * 6.2832, 0.12 + Hash( f + 120 ) * 0.45 );
				float2 halfSize = float2( 0.18, 0.11 ) * ( 0.7 + Hash( f + 140 ) * 0.6 );
				if ( !SpriteUv( dir, centre, halfSize, uv ) )
					continue;

				float4 mask = g_tCloudMasked.SampleLevel( g_sClamp, uv, 0 );
				float3 cloud = lerp( g_vCloudTop, g_vCloudBottom, uv.y ) * mask.rgb;

				float towardsSun = saturate( dot( centre, g_vSunDirection ) );
				float4 hilit = g_tCloudHilit.SampleLevel( g_sClamp, uv, 0 );
				cloud += hilit.rgb * hilit.a * towardsSun * towardsSun * sunAbove;

				color = lerp( color, cloud, mask.a );
			}
		}

		return float4( pow( saturate( color ), 2.2 ), 1.0 );
	}
}
