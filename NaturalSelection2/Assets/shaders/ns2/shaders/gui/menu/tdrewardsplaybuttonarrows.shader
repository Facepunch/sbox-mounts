HEADER
{
	Description = "NS2 tdrewardsplaybuttonarrows";
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
	float g_flStage < Default1( 0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		float4 arrowDistances = float4( 0, 0.0485, 0.089, 0.154 );
		float stageDistance = arrowDistances[g_flStage];

		float clipVal = ( texCoord.x > stageDistance && texCoord.x < ( 1 - stageDistance ) ? -1 : 1 );
		clip( clipVal );

		resultColor = g_tBaseTexture.Sample( g_sSparkWrap, texCoord ) * color;
	}

	#include "ns2/gui_output.hlsl"
}
