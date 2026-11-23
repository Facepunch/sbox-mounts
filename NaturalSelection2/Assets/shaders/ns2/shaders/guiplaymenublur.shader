HEADER
{
	Description = "NS2 guiplaymenublur";
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
	float g_flXFactor < Default1( 0.0 ); >;
	float g_flYFactor < Default1( 0.0 ); >;
	float g_flBlurRadius < Default1( 0.0 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		const float exp = 2.71828182846f;
		const float pi = 3.14159265359f * 2.0f;

		float radiusPx = g_flBlurRadius * g_flResScale;
		int stepCount = min( ceil( radiusPx ), 64 );
		float2 maxOffset = float2( g_flXFactor * g_flRcpFrameX * radiusPx, g_flYFactor * g_flRcpFrameY * radiusPx );
		float maxDist = maxOffset.x + maxOffset.y;
		float maxDistSq = maxDist * maxDist;
		float maxPxDistSq = 1.0 * stepCount * stepCount;

		float totalWeight = 1.0;
		float sum = g_tBaseTexture.Sample( g_sSparkWrap, texCoord ).r;
		for ( int i = 1; i < stepCount; ++i )
		{
			float weight = pow( exp, -0.5 * i * i / ( maxPxDistSq ) );

			float2 co1 = texCoord + ( maxOffset * i ) / stepCount;
			float2 co2 = texCoord - ( maxOffset * i ) / stepCount;
			sum += g_tBaseTexture.Sample( g_sSparkWrap, co1 ).r * weight;
			sum += g_tBaseTexture.Sample( g_sSparkWrap, co2 ).r * weight;

			totalWeight += weight * 2.0;
		}

		float blurResult = sum / totalWeight;

		resultColor = float4( blurResult, 0.0, 0.0, 1.0 );
	}

	#include "ns2/gui_output.hlsl"
}
