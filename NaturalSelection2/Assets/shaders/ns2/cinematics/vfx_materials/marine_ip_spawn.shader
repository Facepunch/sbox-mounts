HEADER
{
	Description = "NS2 marine_ip_spawn";
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
	RenderState( CullMode, NONE );

	CreateInputTexture2D( ScanMap, Linear, 8, "None", "_scanMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tScanMap < Channel( RGBA, Box( ScanMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( ScanMap2, Linear, 8, "None", "_scanMap2", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tScanMap2 < Channel( RGBA, Box( ScanMap2 ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( MaskMap, Linear, 8, "None", "_maskMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaskMap < Channel( RGBA, Box( MaskMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( MaskMap2, Linear, 8, "None", "_maskMap2", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaskMap2 < Channel( RGBA, Box( MaskMap2 ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( MaskMap3, Linear, 8, "None", "_maskMap3", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tMaskMap3 < Channel( RGBA, Box( MaskMap3 ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	CreateInputTexture2D( OverlayMap, Linear, 8, "None", "_overlayMap", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tOverlayMap < Channel( RGBA, Box( OverlayMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flSpawnProgress < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float opacity = 1;
		float ProgressVar = -g_flSpawnProgress * .23;
		float3 mask = Tex2DS( g_tMaskMap, g_sWrap, i.vSparkPosition.y * float2( 1, .5 ) ).rgb;
		float3 animMask =
			Tex2DS( g_tMaskMap2, g_sWrap, i.vSparkPosition.y * float2( 1, .005 ) + float2( 0, ProgressVar - 0.02 ) ).rgb;
		float3 circuitMask =
			Tex2DS( g_tMaskMap3, g_sWrap, i.vSparkPosition.y * float2( 1, .005 ) + float2( 0, ProgressVar ) ).rgb;
		float3 scan = Tex2DS( g_tScanMap, g_sWrap, i.vSparkPosition.y * float2( 1, .125 ) + float2( 0, ProgressVar ) ).rgb;
		float3 circuitScan =
			Tex2DS( g_tScanMap2, g_sWrap, i.vSparkPosition.y * float2( 1, .04 ) + float2( 0, ProgressVar ) ).rgb;
		float3 animCircuit = Tex2DS( g_tOverlayMap, g_sWrap,
									 i.vTextureCoords.xy * float2( 10, 10 ) + float2( g_flTime * -0.15, g_flTime * -0.15 ) )
								 .rgb;
		float3 animCircuit2 = Tex2DS( g_tOverlayMap, g_sWrap, i.vTextureCoords.xy * float2( 8, 5 ) ).rgb;
		emission = scan * 100 * ( ( animCircuit * animCircuit2 ) * circuitMask ) + ( circuitScan * animCircuit * animCircuit2 );
		opacity = ( mask * 5 * animMask ).r;
		return float4( emission, opacity );
	}
}
