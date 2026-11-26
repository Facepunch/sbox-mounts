HEADER
{
	Description = "Source 1 splash droplets";
}

MODES
{
	Forward();
}

COMMON
{
	#define S_TRANSLUCENT 1
	#include "common/shared.hlsl"
}

struct VertexInput
{
	float3 Position : POSITION < Semantic( PosXyz ); >;
	float2 UV : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
	float4 Color : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	float4 Position : SV_Position;
	float2 UV : TEXCOORD0;
	float4 Color : COLOR0;
};

VS
{
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i;
		i.Position = Position3WsToPs( v.Position );
		i.UV = v.UV;
		i.Color = v.Color;
		return i;
	}
}

PS
{
	#include "common/classes/ClusterCulling.hlsl"
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );

	Texture2D g_tDrop < Attribute( "DropTexture" ); SrgbRead( true ); >;
	Texture3D g_tParticleLighting < Attribute( "ParticleLighting" ); SrgbRead( false ); >;
	float3 g_vOrigin < Attribute( "SplashOrigin" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		// All drops pick up the same light from the water surface.
		float4 clip = Position3WsToPs( g_vOrigin );
		float2 uv = clip.xy / max( clip.w, 0.001 ) * float2( 0.5, -0.5 ) + 0.5;
		float depth = clamp( dot( g_vOrigin - g_vCameraPositionWs, g_vCameraDirWs ), ClusterZParams.z, ClusterZParams.w );
		float3 light = g_tParticleLighting.SampleLevel( g_sTrilinearClamp, float3( uv, Cluster::DepthToSlice( depth ) * ClusterInvCounts.z ), 0 ).rgb;
		float3 tint = 0.75 + 0.25 * light / max( 0.001, max( light.r, max( light.g, light.b ) ) );
		float luminosity = clamp( 4 * dot( light, float3( 0.30, 0.59, 0.11 ) ), 0.25, 1 );
		float4 drop = g_tDrop.Sample( g_sTrilinearClamp, i.UV );
		return drop * float4( saturate( tint * i.Color.rgb ), luminosity * i.Color.a );
	}
}
