using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GoldSrc.Bsp;

internal partial class BspFile
{
	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	private readonly struct ClipNode
	{
		public readonly int Plane;
		public readonly ushort Front;
		public readonly ushort Back;
	}

	private ClipNode[] ClipNodes;

	// GoldSrc traces people through the standing or crouched hull, offsetting the origin by
	// the difference between the compiled crouch hull and the monster's own mins.
	internal Vector3? DropToHumanHull( Vector3 origin, Vector3 mins, bool crouched )
	{
		var head = crouched ? Models[0].HeadNode3 : Models[0].HeadNode1;
		if ( head >= ClipNodes.Length ) return null;
		var start = origin + mins - new Vector3( -16, -16, crouched ? -18 : -36 );
		var distance = FirstSolid( (int)head, start, 0, 256, 0 );
		return distance.HasValue ? origin + Vector3.Down * Math.Max( 0, distance.Value - 1f / 32 ) : null;
	}

	private float? FirstSolid( int index, Vector3 start, float from, float to, int depth )
	{
		if ( index >= ClipNodes.Length )
			return (short)index == -2 ? from : null; // CONTENTS_SOLID
		if ( depth >= 256 ) throw new InvalidDataException( "Invalid GoldSrc clipnode tree." );
		var node = ClipNodes[index];
		var plane = Planes[node.Plane];
		var top = Vector3.Dot( start, plane.Normal ) - plane.Distance;
		var front = top - from * plane.Normal.z >= 0;
		int near = front ? node.Front : node.Back;
		if ( front == (top - to * plane.Normal.z >= 0) )
			return FirstSolid( near, start, from, to, depth + 1 );

		var split = Math.Clamp( top / plane.Normal.z, from, to );
		return FirstSolid( near, start, from, split, depth + 1 )
			?? FirstSolid( front ? node.Back : node.Front, start, split, to, depth + 1 );
	}
}
