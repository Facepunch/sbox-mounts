HEADER
{
	Description = "NS2 mainbarbuttonglow";
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
	float4 g_vTextColor < Default4( 1, 1, 1, 1 ); >;
	CreateInputTexture2D( InnerGlowBlur, Linear, 8, "None", "_innerGlowBlur", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tInnerGlowBlur < Channel( RGBA, Box( InnerGlowBlur ), Linear ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	float4 g_vInnerGlowColor < Default4( 1, 1, 1, 1 ); >;
	CreateInputTexture2D( OuterGlowBlur, Linear, 8, "None", "_outerGlowBlur", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tOuterGlowBlur < Channel( RGBA, Box( OuterGlowBlur ), Linear ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	float4 g_vOuterGlowColor < Default4( 1, 1, 1, 1 ); >;

	float4 Over( float4 A, float4 B )
	{
		return A + B * ( 1.0f - A.a );
	}
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
		const float gradientOpacity = 0.34f;
		const float gradientPinch = 0.25f;

		float textRead = g_tBaseTexture.Sample( g_sSparkWrap, texCoord ).r;
		float innerGlowRead = g_tInnerGlowBlur.Sample( g_sSparkWrap, texCoord ).r;
		float outerGlowRead = g_tOuterGlowBlur.Sample( g_sSparkWrap, texCoord ).r;

		float4 text = Premult( float4( 1, 1, 1, textRead ) );
		text *= g_vTextColor;

		float4 innerGlow = float4( 1, 1, 1, innerGlowRead );
		innerGlow.a = ( 1.0f - innerGlow.a ) * textRead;
		innerGlow = Premult( innerGlow );
		innerGlow *= g_vInnerGlowColor;

		float4 outerGlow = float4( 1, 1, 1, outerGlowRead );
		outerGlow.a *= 1.0f - textRead;
		outerGlow *= g_vOuterGlowColor;
		outerGlow = Premult( outerGlow );

		float grad = saturate( ( ( texCoord.y - 0.5f ) / gradientPinch ) + 0.5f );
		float4 gradient = Premult( float4( grad, grad, grad, gradientOpacity * textRead ) );

		text = Over( gradient, text );
		text += innerGlow;
		text = Over( outerGlow, text );

		resultColor = Unpremult( text * color );
	}

	#include "ns2/gui_output.hlsl"
}
