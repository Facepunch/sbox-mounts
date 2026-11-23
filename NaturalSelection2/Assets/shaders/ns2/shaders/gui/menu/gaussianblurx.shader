HEADER
{
	Description = "NS2 gaussianblurx";
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
	float g_flBlurRadius < Default1( 0.0 ); >;
	float g_flScaleFactorX < Default1( 1.0 ); >;
	float g_flRcpFrameX < Default1( 1.0 ); >;

	float4 Premult( float4 input )
	{
		input.rgb *= input.a;
		return input;
	}
	float4 Unpremult( float4 input )
	{
		input.rgb = input.a > 0.0f ? input.rgb / input.a : 0.0f;
		return input;
	}

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		const float exp = 2.71828182846f;

		float radiusPx = g_flBlurRadius * g_flScaleFactorX;
		int stepCount = min( ceil( radiusPx ), 64 );
		float maxOffset = g_flRcpFrameX * radiusPx;
		float maxPxDistSq = float( stepCount ) * stepCount;

		float totalWeight = 1.0;
		float4 sum = Premult( g_tBaseTexture.Sample( g_sSparkWrap, texCoord ) );
		for ( int i = 1; i <= stepCount; ++i )
		{
			float weight = pow( exp, -0.5 * i * i / maxPxDistSq );
			float2 co_one = texCoord + float2( maxOffset * i, 0.0f ) / stepCount;
			float2 co_two = texCoord - float2( maxOffset * i, 0.0f ) / stepCount;
			sum += Premult( g_tBaseTexture.Sample( g_sSparkWrap, co_one ) ) * weight;
			sum += Premult( g_tBaseTexture.Sample( g_sSparkWrap, co_two ) ) * weight;

			totalWeight += weight * 2.0;
		}

		resultColor = Unpremult( sum / totalWeight ) * color;
	}

	#include "ns2/gui_output.hlsl"
}
