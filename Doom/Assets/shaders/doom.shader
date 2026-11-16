
HEADER
{
	Description = "";
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
	float4 vColor : COLOR0 < Semantic( Color ); >;
	float4 vLightA : TEXCOORD2 < Semantic( None ); >; // type, max, min, phase
    float4 vLightB : TEXCOORD3 < Semantic( None ); >; // bright tics, dark tics, sector id, face adjust
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vColor : COLOR0;
	
    nointerpolation float4 vLightA : TEXCOORD14;
    nointerpolation float4 vLightB : TEXCOORD15;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		i.vLightA = v.vLightA;
		i.vLightB = v.vLightB;	
		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"

	float4 g_SheetData < Attribute( "BaseTextureSheet" ); >;
	SamplerState g_sSampler0 < Filter( Point ); AddressU( WRAP ); AddressV( WRAP ); >;
	
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2DArray  g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

	BoolAttribute( alphatest, true );

	// 35 Hz
	float g_flFps < Attribute( "FramesPerSecond" ); Default1( 4.375 ); >;

	uint HashU( uint x )
	{
		x ^= x >> 16; x *= 0x7feb352du;
		x ^= x >> 15; x *= 0x846ca68bu;
		x ^= x >> 16;
		return x;
	}

	// Effect types: 0 steady, 1 flicker, 2 strobe, 3 glow, 4 fire flicker
	float EvalSectorLight( float4 a, float4 b )
	{
		int   type   = (int)round( a.x );
		float maxL   = a.y;
		float minL   = a.z;
		float phase  = a.w;
		uint  sector = (uint)round( b.z );

		float tics = g_flTime * 35.0;
		uint  tic  = (uint)tics;

		if ( type == 1 ) // flicker
		{
			uint h = HashU( sector * 9781u + tic / 4u );
			return ( h % 100u ) < 21u ? minL : maxL;
		}

		if ( type == 2 ) // strobe
		{
			float period = b.x + b.y;
			float t = fmod( tics + phase, period );
			return t < b.x ? maxL : minL;
		}

		if ( type == 3 ) // glow
		{
			float half = max( ( maxL - minL ) / 8.0, 0.001 );
			float t = fmod( floor( tics ) + phase, half * 2.0 );
			float v = t < half ? t : half * 2.0 - t;
			return maxL - v * 8.0;
		}

		if ( type == 4 ) // fire flicker
		{
			uint h = HashU( sector * 6151u + tic / 4u );
			float amount = float( h & 3u ) * 16.0;
			return ( maxL - amount < minL ) ? minL : maxL - amount;
		}

		return maxL;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );
				
		uint width, height, arrayCount;
		g_tColor.GetDimensions( width, height, arrayCount);
		float slice = fmod( floor( g_flTime * g_flFps ), arrayCount );

		float4 color = g_tColor.Sample( g_sSampler0, float3( i.vTextureCoords.xy, slice ) );
		clip( color.a - 0.5 );

		float light = saturate( ( EvalSectorLight( i.vLightA, i.vLightB ) + i.vLightB.w ) / 255.0 );
		m.Albedo = color.rgb;
		m.Normal = i.vNormalWs;
		m.TextureCoords = i.vTextureCoords.xy;
		m.Roughness = 1;
		m.Metalness = 0;
		m.AmbientOcclusion = pow( light, 2 );
		m.TintMask = 1;
		m.Opacity = 1;
		m.Emission = 0;
		m.Transmission = 0;

		return ShadingModelStandard::Shade( i, m );
	}
}
