HEADER
{
	Description = "NS2 command_station_display_atlased";
}
FEATURES
{
	#include "common/features.hlsl"
}
MODES
{
	Forward();
}
COMMON
{
	#include "common/shared.hlsl"
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
	#include "ns2/surface_vertex.hlsl"
}
PS
{
	#include "ns2/emissive_pass.hlsl"
	RenderState( DstBlend, ONE );

	CreateInputTexture2D( EmissiveMap, Linear, 8, "None", "_emissiveMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tEmissiveMap < Channel( RGBA, Box( EmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( WelcomeEmissiveMap, Linear, 8, "None", "_welcomeEmissiveMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tWelcomeEmissiveMap < Channel( RGBA, Box( WelcomeEmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( LockedEmissiveMap, Linear, 8, "None", "_lockedEmissiveMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tLockedEmissiveMap < Channel( RGBA, Box( LockedEmissiveMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flHiddenAmount < Default1( 0.0 ); >;
	float g_flTimeStateChanged < Default1( 0.0 ); >;
	float g_flState < Default1( 1.0 ); >;
	float g_flTextureIndex < Default1( 0 ); >;
	float g_flNumRows < Default1( 2 ); >;
	float g_flNumColumns < Default1( 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float f = 1 - g_flHiddenAmount;
		const float normal = 1;
		const float locked = 2;
		const float welcome = 3;
		const float destroyed = 5;
		float2 uv = i.vTextureCoords.xy;
		float row = floor( g_flTextureIndex / g_flNumColumns );
		float col = g_flTextureIndex - row * g_flNumColumns;
		uv.x = ( col + uv.x ) / g_flNumColumns;
		uv.y = ( row + uv.y ) / g_flNumRows;
		float4 tex = 0;
		if ( g_flState == normal )
		{
			tex = SampleSrgb( g_tEmissiveMap, g_sWrap, uv );
		}
		else if ( g_flState == locked )
		{
			tex = SampleSrgb( g_tLockedEmissiveMap, g_sWrap, uv );
		}
		else if ( g_flState == welcome )
		{
			tex = SampleSrgb( g_tWelcomeEmissiveMap, g_sWrap, uv );
		}
		if ( g_flState == destroyed )
		{
			emission = float3( 0, 0, 0 );
			opacity = 0;
		}
		else
		{
			emission = tex.rgb * i.vColor.rgb * ( f + 1 + cos( g_flTime * 4 ) );
			opacity = tex.a * i.vColor.a;
		}
		return float4( emission, opacity );
	}
}
