HEADER
{
	Description = "NS2 guiobjectivedisplay";
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
	CreateInputTexture2D( ScanlineTex, Linear, 8, "None", "_scanlineTex", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tScanlineTex < Channel( RGBA, Box( ScanlineTex ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( Mask, Linear, 8, "None", "_mask", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMask < Channel( RGBA, Box( Mask ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( TransitionMap, Linear, 8, "None", "_transitionMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tTransitionMap < Channel( RGBA, Box( TransitionMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flStartTime < Default1( 0 ); >;
	float g_flEndTime < Default1( 1 ); >;
	float g_flStartPosition < Default1( 2 ); >;
	float g_flEndPosition < Default1( 2 ); >;

	void MaterialShader( bool textured, float2 texCoord, float4 color, inout float4 resultColor )
	{

		float trans_i = saturate( ( g_flTime - g_flStartTime ) / ( g_flEndTime - g_flStartTime ) );
		float trans_pos = g_flStartPosition * ( 1.0 - trans_i ) + g_flEndPosition * trans_i;
		float2 trans_coords = float2( texCoord.x, ( texCoord.y * 0.2 ) + ( 0.4 * trans_pos ) );
		float trans_val = g_tTransitionMap.Sample( g_sSparkWrap, trans_coords ).a * 3.984375;

		float4 scanlines =
			g_tScanlineTex.Sample( g_sSparkWrap, float2( texCoord.x, ( texCoord.y * 0.5021 ) + g_flTime * 0.01 ) );
		scanlines.a *= g_tMask.Sample( g_sSparkWrap, texCoord ).a * 0.20;
		scanlines.rgb *= scanlines.a;

		float4 frame = g_tBaseTexture.Sample( g_sSparkWrap, texCoord );

		float4 composite;
		composite.rgb = scanlines.rgb + frame.rgb * ( 1.0 - scanlines.a );
		composite.a = scanlines.a * ( 1.0 - frame.a ) + frame.a;

		composite.rgb = saturate( composite.rgb / ( saturate( composite.a ) + 0.0001 ) );

		composite.a *= trans_val;

		resultColor = composite;
	}

	#include "ns2/gui_output.hlsl"
}
