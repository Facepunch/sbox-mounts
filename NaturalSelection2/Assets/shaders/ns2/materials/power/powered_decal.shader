HEADER
{
	Description = "NS2 powered_decal";
}
FEATURES
{
	#include "common/features.hlsl"
}
MODES
{
	Forward();
}
COMMON
{
	#include "common/shared.hlsl"
}
struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vColor : COLOR0;
	float3 vSparkPosition : TEXCOORD14;
};
VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		i.vSparkPosition = v.vPositionOs.yzx / 40.0;
		return FinalizeVertex( i );
	}
}
PS
{
	#include "ns2/emissive_pass.hlsl"
	RenderState( DstBlend, ONE );

	CreateInputTexture2D( MaskMap, Linear, 8, "None", "_maskMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaskMap < Channel( RGBA, Box( MaskMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( MaskMap2, Linear, 8, "None", "_maskMap2", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaskMap2 < Channel( RGBA, Box( MaskMap2 ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float3 g_vOsOrigin < Default3( 0, 0, 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float3 d = g_vOsOrigin - i.vSparkPosition;
		float r = length( d.xz ) / 10;
		const float waveFreq = 5;
		const float waveSpeed = 1.5;
		const float wavePow = 90;
		const float wavePow2 = 1;
		const float wavePow3 = 200;
		const float texScale = 0.2;
		const float maskScale = 50;
		const float maskScale2 = 5;
		float w = 1 - fmod( g_flTime * waveSpeed - r, waveFreq ) / waveFreq;
		float wave = pow( w, wavePow );
		float wave2 = pow( w, wavePow2 );
		float wave3 = pow( w, wavePow3 ) * 0.03;
		float mask = Tex2DS( g_tMaskMap, g_sClamp, ( d.xz / maskScale ) + 0.5 ).r * 2;
		float mask2 = Tex2DS( g_tMaskMap2, g_sWrap, ( d.xz / maskScale2 ) + 0.5 ).r * 0.2 + 0.1;
		const float3 color = float3( 0, 1, 2 );
		emission = ( wave * mask + wave2 * mask2 * 0.1 + wave3 ) * color;
		return float4( emission, opacity );
	}
}
