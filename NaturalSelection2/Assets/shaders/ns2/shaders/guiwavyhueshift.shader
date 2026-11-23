HEADER
{
	Description = "NS2 guiwavyhueshift";
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
	float g_flTimeOffset < Default1( 0.0 ); >;
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

	float wave( float x, float t, float u, float y )
	{
		u = u * u * u;
		float a = 6 * 3.14159;
		float period = a * cos( cos( t / 2 ) );
		float value = 4 * a * x + cos( period * x ) * ( 0.5 * t + 1 ) - 1;

		return u * value + ( 1 - u ) * x;
	}

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		float4 readIn = g_tBaseTexture.Sample( g_sSparkWrap, texCoord );
		float t = ( g_flTime + g_flTimeOffset ) * 2;
		const float amountMult = 3.0f;

		float amount = ( readIn.r * 0.21f + readIn.g * 0.72f + readIn.b * 0.07f ) * readIn.a * amountMult;
		float2 resultCoord;
		resultCoord.x = wave( texCoord.x, cos( t ), amount, texCoord.y ) / wave( 1.0, cos( t ), amount, texCoord.y );
		resultCoord.y =
			wave( texCoord.y, 1.2 * sin( t ), amount, texCoord.x ) / wave( 1.0, 1.2 * sin( t ), amount, texCoord.x );
		float4 result = g_tBaseTexture.Sample( g_sSparkWrap, resultCoord );

		float3 hsv = RGBtoHSV( result.rgb );
		hsv.x += g_flHueOffset;
		hsv.x = hsv.x - floor( hsv.x );
		result.rgb = HSVtoRGB( hsv );

		resultColor = result;
	}

	#include "ns2/gui_output.hlsl"
}
