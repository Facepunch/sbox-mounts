HEADER
{
	Description = "Halo 2 lens occlusion";
}
MODES
{
	Default();
}
COMMON
{
	#include "system.fxc"
}
CS
{
	#include "common.fxc"
	#include "math_general.fxc"
	#include "common/classes/Depth.hlsl"

	float4 LensProbe < Attribute( "LensProbe" ); >;
	RWStructuredBuffer<float4> LensHistory < Attribute( "LensHistory" ); >;
	groupshared uint samples[64];

	[numthreads( 8, 8, 1 )]
	void MainCs( uint3 lane : SV_GroupThreadID, uint index : SV_GroupIndex )
	{
		int2 first = int2( floor( LensProbe.xy - LensProbe.z ) );
		int2 last = int2( floor( LensProbe.xy + LensProbe.z ) );
		int2 size = max( last - first, 0 );
		int2 lo = max( first, 0 );
		int2 hi = min( last, int2( g_vViewportSize ) );
		uint visible = 0;
		for ( int y = lo.y + lane.y; y < hi.y; y += 8 )
			for ( int x = lo.x + lane.x; x < hi.x; x += 8 )
				visible += Depth::GetLinear( int2( x, y ) ) >= LensProbe.w;

		samples[index] = visible;
		GroupMemoryBarrierWithGroupSync();
		for ( uint stride = 32; stride > 0; stride >>= 1 )
		{
			if ( index < stride ) samples[index] += samples[index + stride];
			GroupMemoryBarrierWithGroupSync();
		}
		if ( index == 0 )
		{
			float fraction = samples[0] / max( 1.0, float( size.x ) * size.y );
			float4 history = LensHistory[0];
			// The completed query is two submissions old. TU1.5 updates visibility in bytes.
			float current = round( history.x * 255 );
			float target = floor( history.y * 255 );
			float filtered = floor( lerp( current, target, target > current ? 0.125 : 0.5 ) );
			LensHistory[0] = float4( filtered / 255, history.z, fraction, 0 );
		}
	}
}
