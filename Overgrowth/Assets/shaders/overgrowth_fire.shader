HEADER
{
	Description = "Overgrowth character flame ribbon";
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
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 flame : TEXCOORD14;
};
VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput o = ProcessVertex( v );
		o.flame = float4( v.vTexCoord, v.vTexCoord2 );
		return FinalizeVertex( o );
	}
}
PS
{
	#include "common/pixel.hlsl"
	BoolAttribute( translucent, true );
	RenderState( CullMode, NONE );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
	RenderState( DepthWriteEnable, false );

	float FlameHash( float2 p )
	{
		return -1.0 + 2.0 * frac( sin( dot( p, float2(127.1,311.7) ) ) * 43758.5453123 );
	}
	float FlameNoise( float2 p )
	{
		float2 i = floor( p ), f = frac( p );
		float2 u = f*f*(3-2*f);
		return lerp( lerp( FlameHash(i), FlameHash(i+float2(1,0)), u.x ),
			lerp( FlameHash(i+float2(0,1)), FlameHash(i+1), u.x ), u.y );
	}
	float Fractal( float2 p )
	{
		p = frac( p / 100 + 0.5 ) * 100;
		float sum = 0, weight = 0.5;
		[unroll] for ( int i = 0; i < 6; i++ )
		{
			sum += (i == 5 ? 0.016625 : weight) * FlameNoise( p );
			p = float2( 1.6*p.x - 1.2*p.y, 1.2*p.x + 1.6*p.y );
			weight *= 0.5;
		}
		return sum;
	}
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 world = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 ray = normalize( world - g_vCameraPositionWs );
		float heat = max( 0, i.flame.x ), u = i.flame.y - 0.5;
		float born = i.flame.z, now = i.flame.w;
		float2 p = float2( u + ray.y*0.2, (born + ray.x*0.02)*10 );
		float noise = Fractal(p);
		p.y += now*3;
		float noise2 = (Fractal(p*3) + Fractal(p*-2))*0.5;
		u += noise*0.3 + noise2*0.3 + 0.15;
		float width = min( pow(heat,0.6), 1 ) * 0.25;
		float alpha = abs(u) > width
			? pow( saturate(1-abs(u)+width), 60 ) * saturate((i.flame.x+0.02)*10)
			: max( pow(abs(u)/max(width,0.00001),2), saturate((0.1-heat)*10) ) + pow( abs(0.5-abs(noise2)), 2 )*8;
		float depth = dot( world-g_vCameraPositionWs, g_vCameraDirWs ) / 40;
		float3 behind = Depth::GetWorldPosition( i.vPositionSs.xy );
		float soft = saturate( dot( behind-world, g_vCameraDirWs ) * (3.0/40) );
		float age = now-born;
		float fade = saturate((age-0.1)*20) * (1-saturate((age-0.3)*20)) * soft;
		alpha = max(0,alpha*0.25 + fade-1) * saturate(depth-0.4) * sqrt(heat);
		float3 normal = normalize( cross(ddx(world),ddy(world)) );
		alpha *= saturate( (abs(dot(normal,ray))-0.4)*2 );
		return float4(6-heat,sqrt(heat)*3,max(0,(heat*2.5)*(heat*2.5)-1)*0.5,alpha);
	}
}
