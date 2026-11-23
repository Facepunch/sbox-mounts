HEADER
{
	Description = "NS2 guihueshift";
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
	Texture2D g_tBaseTexture < Attribute( "Texture" ); Channel( RGBA, Box( BaseTexture ), Linear ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	float g_flHueOffset < Default1( 0.0 ); >;

	float3 HUEtoRGB( float h )
	{
		float r = abs( h * 6 - 3 ) - 1;
		float g = 2 - abs( h * 6 - 2 );
		float b = 2 - abs( h * 6 - 4 );
		return saturate( float3( r, g, b ) );
	}

	float Epsilon = 1e-10;
	float3 RGBtoHCV( float3 rgb )
	{
		float4 p = ( rgb.g < rgb.b ) ? float4( rgb.bg, -1.0, 2.0 / 3.0 ) : float4( rgb.gb, 0.0, -1.0 / 3.0 );
		float4 q = ( rgb.r < p.x ) ? float4( p.xyw, rgb.r ) : float4( rgb.r, p.yzx );
		float c = q.x - min( q.w, q.y );
		float h = abs( ( q.w - q.y ) / ( 6 * c + Epsilon ) + q.z );
		return float3( h, c, q.x );
	}

	float3 RGBtoHSV( float3 rgb )
	{
		float3 hcv = RGBtoHCV( rgb );
		float s = hcv.y / ( hcv.z + Epsilon );
		return float3( hcv.x, s, hcv.z );
	}

	float3 HSVtoRGB( float3 hsv )
	{
		float3 rgb = HUEtoRGB( hsv.x );
		return ( ( rgb - 1 ) * hsv.y + 1 ) * hsv.z;
	}

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		float4 readIn = g_tBaseTexture.Sample( g_sSparkWrap, texCoord );

		float3 hsb = RGBtoHSV( readIn.rgb );
		hsb.x += g_flHueOffset;
		hsb.x = hsb.x - floor( hsb.x );

		resultColor = float4( HSVtoRGB( hsb ), readIn.a ) * color;
	}

	#include "ns2/gui_output.hlsl"
}
