HEADER
{
	Description = "NS2 infestation";
}
FEATURES
{
	#include "common/features.hlsl"
}
MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}
COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
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
};
VS
{
	#include "common/vertex.hlsl"
	float g_flAmount < Default( 0 ); >;
	float g_flMaxRadius < Default( 0 ); >;
	float3 g_vOrigin < Default3( 0, 0, 0 ); >;
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		float3 position = i.vPositionWs.xyz;
		float3 sparkPosition = position.yzx / 40.0;
		float scale = saturate( ( g_flAmount * g_flMaxRadius - distance( sparkPosition, g_vOrigin ) ) * 0.5 );
		float3 pivot = mul( GetTransformMatrix( v.nInstanceTransformID, v.nBoneIndex.x ), float4( 0, 0, 0, 1 ) );
		float3 wave = sin( g_flTime * float3( 2, 3, 1 ) + sparkPosition.zyx );
		i.vPositionWs.xyz = lerp( pivot, position, scale ) + wave * ( 0.8 * g_flAmount );
		i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );
		i.vColor = v.vColor;
		return FinalizeVertex( i );
	}
}
PS
{
	#include "ns2/surface_lighting.hlsl"

	CreateInputTexture2D( Albedo, Linear, 8, "None", "_albedo", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tAlbedo < Channel( RGBA, Box( Albedo ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 emission = 0;
		float3 sparkAlbedo = 0;
		float3 sparkSpecular = 0;
		float sparkGloss = 0;
		float3 sparkNormal = float3( 0, 0, 1 );
		float opacity = 1;
		float4 albedo = Tex2DS( g_tAlbedo, g_sWrap, i.vTextureCoords.xy );
		sparkAlbedo = albedo.rgb;
		opacity = albedo.a;
		return ShadeSparkSurface( i, sparkAlbedo, sparkSpecular, sparkGloss, sparkNormal, emission * opacity );
	}
}
