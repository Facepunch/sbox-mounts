HEADER
{
	Description = "NS2 guiplaymenuscanlinesmult";
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
	float g_flRcpFrameX < Default1( 0.0 ); >;
	float g_flRcpFrameY < Default1( 0.0 ); >;
	float g_flResScale < Default1( 0.0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		const float scanlineSpeed = 0.01f;
		const float scanlineFreq = 2.0f;

		float angle = texCoord.y * scanlineFreq / g_flRcpFrameY + g_flTime * scanlineSpeed / g_flRcpFrameY;
		float scanlines1 = sin( angle ) * 0.5 + 0.5;
		float scanlines2 = ( 0.125 * sin( angle * 2 ) * 0.5 + 0.5 ) + 0.875;
		float scanlines3 = ( 0.5 * sin( angle * 4 ) * 0.5 + 0.5 ) + 0.5;
		float scanlines4 = ( 0.25 * sin( angle * 8 ) * 0.5 + 0.5 ) + 0.75;
		float scanlines5 = ( 0.25 * sin( angle * 16 ) * 0.5 + 0.5 ) + 0.75;

		float scanlines = pow( ( scanlines1 * scanlines2 * scanlines3 * scanlines4 * scanlines5 ), 0.8 );
		float alpha = scanlines * color.a * g_tBaseTexture.Sample( g_sSparkWrap, texCoord ).a;
		resultColor = float4( float3( 1, 1, 1 ) * ( 1.0 - alpha ) + color.rgb * alpha, 0.0 );
	}

	#include "ns2/gui_output.hlsl"
}
