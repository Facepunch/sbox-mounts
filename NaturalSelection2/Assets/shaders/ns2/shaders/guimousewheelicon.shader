HEADER
{
	Description = "NS2 guimousewheelicon";
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
	float g_flRIntensity < Default1( 0 ); >;
	float g_flGIntensity < Default1( 0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		float pulse = saturate( 0.5 - ( cos( g_flTime * 3.14159265 * 2 ) * 0.5 ) );
		float4 base = g_tBaseTexture.Sample( g_sSparkWrap, texCoord );
		float intensity = ( 1.0 - ( ( 1.0 - ( g_flRIntensity * base.r ) ) * ( 1.0 - ( g_flGIntensity * base.g ) ) ) ) * pulse;
		float alpha = base.b * color.a + intensity;
		float3 unmultColor = color.rgb + ( intensity * ( 1.0 - base.b ) );
		resultColor = float4( unmultColor.r, unmultColor.g, unmultColor.b, alpha );
	}

	#include "ns2/gui_output.hlsl"
}
