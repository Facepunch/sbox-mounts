HEADER
{
	Description = "NS2 gradient";
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
	float4 g_vColor1 < Default4( 1, 1, 1, 1 ); >;
	float2 g_vColor1Origin < Default2( 0, 0 ); >;
	float4 g_vColor2 < Default4( 0, 0, 0, 1 ); >;
	float2 g_vColor2Origin < Default2( 0, 1 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{
		float2 diff = g_vColor2Origin - g_vColor1Origin;
		float distSq = diff.x * diff.x + diff.y * diff.y;
		float4 gradColor;
		if ( distSq > 0 )
		{
			float2 pDiff = texCoord - g_vColor1Origin;
			float fraction = saturate( dot( pDiff, diff ) / distSq );
			gradColor = g_vColor1 * ( 1.0f - fraction ) + g_vColor2 * fraction;
		}
		else
		{
			gradColor = ( g_vColor1 + g_vColor2 ) * 0.5f;
		}

		resultColor = gradColor * color;
	}

	#include "ns2/gui_output.hlsl"
}
