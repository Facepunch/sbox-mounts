
HEADER
{
	Description = "Doom sky";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth();
}

COMMON
{
	#include "common/shared.hlsl"

	#define CUSTOM_MATERIAL_INPUTS
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		return FinalizeVertex( i );
	}
}

PS
{
    #include "common/pixel.hlsl"

    SamplerState g_sSampler0 < Filter( Point ); AddressU( WRAP ); AddressV( CLAMP ); >;

    CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
    Texture2DArray g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

    #define TAU 6.28318530718
    #define SKY_REPEATS 4.0
    #define SKY_HORIZON_ROW 100.0
    #define SKY_DEG_PER_ROW 0.422

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float3 vDir = normalize( i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs.xyz );

        float flAzimuth = atan2( vDir.y, vDir.x );
        float flElevation = degrees( asin( clamp( vDir.z, -1.0, 1.0 ) ) );

        uint w, h, n;
        g_tColor.GetDimensions( w, h, n );

        float u = -flAzimuth / TAU * SKY_REPEATS;
        float v = ( SKY_HORIZON_ROW - flElevation / SKY_DEG_PER_ROW ) / h;

        float3 vColor = g_tColor.Sample( g_sSampler0, float3( u, v, 0 ) ).rgb;
        return float4( vColor, 1.0 );
    }
}