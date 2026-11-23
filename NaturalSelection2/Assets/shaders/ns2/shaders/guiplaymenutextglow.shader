HEADER
{
	Description = "NS2 guiplaymenutextglow";
}
MODES
{
	Default();
	Forward();
}
FEATURES
{
	#include "ui/features.hlsl"
}
COMMON
{
	#include "ui/common.hlsl"
}
VS
{
	#include "ui/vertex.hlsl"
}
PS
{
	#include "ns2/gui_pixel.hlsl"
	CreateInputTexture2D( BaseTexture, Linear, 8, "None", "_baseTexture", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tBaseTexture < Attribute( "Texture" ); Channel( RGBA, Box( BaseTexture ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( GlowMap, Linear, 8, "None", "_glowMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGlowMap < Channel( RGBA, Box( GlowMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flRcpFrameX < Default1( 0.0 ); >;
	float g_flRcpFrameY < Default1( 0.0 ); >;
	float g_flResScale < Default1( 0.0 ); >;
	float g_flGlowRadius < Default1( 13.0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		const float4 glowColor = pow( float4( 0.0, 102.0 / 255.0, 1.0, 1.0 ), 1.0 / 2.2 );

		const float4 textColor0 = float4( 56.0 / 255.0, 239.0 / 255.0, 1.0, 1.0 );
		const float4 textColor1 = float4( 0.510, 1.23, 1.0, 1.0 );

		float glowRead = pow( g_tGlowMap.Sample( g_sSparkWrap, texCoord ).r, 1.0 / 2.2 );
		float4 glow = glowColor * float4( 1.0, 1.0, 1.0, glowRead );

		float base = g_tBaseTexture.Sample( g_sSparkWrap, texCoord ).r;
		float4 baseColor = saturate( float4( textColor1.rgb * texCoord.y + textColor0.rgb * ( 1.0 - texCoord.y ), base ) );
		baseColor = float4( baseColor.rgb * baseColor.a, baseColor.a );

		glow.a *= 1.0 - base;
		glow = float4( glow.rgb * glow.a, glow.a );
		float4 composite = baseColor + glow;

		if ( composite.a <= 0.0 )
		{
			composite = float4( 0, 0, 0, 0 );
		}
		else
		{
			composite = float4( composite.rgb / composite.a, composite.a );
		}

		resultColor = composite;
	}

	#include "ns2/gui_output.hlsl"
}
