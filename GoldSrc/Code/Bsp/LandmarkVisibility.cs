namespace GoldSrc.Bsp;

/// <summary>The BSP planes and visibility row needed to test moving entities against a landmark's PVS.</summary>
internal sealed class LandmarkVisibility( Node[] nodes, Plane[] planes, byte[] visible )
{
	internal static LandmarkVisibility From( BspFile bsp, Vector3 origin )
	{
		if ( bsp.Nodes.Length == 0 ) return null;
		int index = 0;
		while ( index >= 0 )
		{
			var node = bsp.Nodes[index];
			var plane = bsp.Planes[node.PlaneNum];
			index = node.Child( Vector3.Dot( plane.Normal, origin ) > plane.Distance ? 0 : 1 );
		}
		var offset = bsp.Leaves[~index].VisOffset;
		if ( offset < 0 || bsp.Visibility.Length == 0 ) return null;

		// A zero byte is followed by the number of empty bytes in the run.
		var row = new byte[(bsp.Models[0].VisLeafCount + 7) / 8];
		for ( int i = 0; i < row.Length && offset < bsp.Visibility.Length; )
		{
			var value = bsp.Visibility[offset++];
			if ( value != 0 ) row[i++] = value;
			else if ( offset < bsp.Visibility.Length ) i += bsp.Visibility[offset++];
		}
		return new( bsp.Nodes, bsp.Planes, row );
	}

	internal bool Contains( BBox bounds ) => Intersects( 0, bounds.Center, bounds.Size * 0.5f );

	private bool Intersects( int index, Vector3 center, Vector3 halfSize )
	{
		if ( index < 0 )
		{
			// Leaf zero is solid and has no bit in the PVS.
			int bit = ~index - 1;
			return bit >= 0 && bit / 8 < visible.Length && (visible[bit / 8] & (1 << (bit % 8))) != 0;
		}
		var node = nodes[index];
		var plane = planes[node.PlaneNum];
		float distance = Vector3.Dot( plane.Normal, center ) - plane.Distance;
		float extent = Vector3.Dot( plane.Normal.Abs(), halfSize );
		return (distance + extent >= 0 && Intersects( node.Child0, center, halfSize ))
			|| (distance - extent <= 0 && Intersects( node.Child1, center, halfSize ));
	}
}
