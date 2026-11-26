using Sandbox.Navigation;
using System;

namespace NS2;

internal struct VentDrop
{
	public Vector3 Landing { get; private set; }
	TimeUntil expires;

	public void Find( Vector3 origin, IReadOnlyList<NavMeshPathPoint> points, int start = 0 )
	{
		const float reach = 3 * MapLoader.Scale;
		float high = float.NegativeInfinity;
		Vector3? landing = null;
		for ( int i = start; i < points.Count; i++ )
		{
			var point = points[i].Position;
			if ( (point - origin).WithZ( 0 ).LengthSquared > reach * reach ) break;
			high = Math.Max( high, point.z );
			if ( high - point.z >= 1.5f * MapLoader.Scale && (landing is null || point.z < landing.Value.z) )
				landing = point;
		}
		if ( landing is { } floor )
		{
			Landing = floor;
			expires = 2.5f;
		}
	}

	public bool Active( Vector3 origin )
	{
		if ( expires > 0 && origin.z - Landing.z > 0.6f * MapLoader.Scale ) return true;
		expires = 0;
		return false;
	}
}
