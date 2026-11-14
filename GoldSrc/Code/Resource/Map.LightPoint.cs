using System;
using System.IO;
using GoldSrc.Bsp;

namespace GoldSrc;

partial class MapLoader
{
	// Sample the floor, not the mean lighting of a room's walls.
	private Vector3 SampleFloorLight( BspFile bsp, Vector3 position )
	{
		if ( bsp.Lighting.Length == 0 ) return new Vector3( 255 );
		var start = position + Vector3.Up * 8;
		return TraceLightPoint( bsp, checked( (int)bsp.Models[0].HeadNode0 ), start, start + Vector3.Down * 2048, 0, out var color )
			? color.Clamp( 0, 255 ) : Vector3.Zero;
	}

	private bool TraceLightPoint( BspFile bsp, int index, Vector3 start, Vector3 end, int depth, out Vector3 color )
	{
		color = Vector3.Zero;
		if ( index < 0 ) return false;
		if ( index >= bsp.Nodes.Length || depth >= 128 )
			throw new InvalidDataException( $"{FilePath}: invalid BSP tree while sampling model lighting." );
		var node = bsp.Nodes[index];
		var plane = bsp.Planes[node.PlaneNum];
		float front = Vector3.Dot( start, plane.Normal ) - plane.Distance;
		float back = Vector3.Dot( end, plane.Normal ) - plane.Distance;
		int side = front < 0 ? 1 : 0;
		if ( (back < 0) == (front < 0) )
			return TraceLightPoint( bsp, node.Child( side ), start, end, depth + 1, out color );
		var middle = start + (end - start) * (front / (front - back));
		if ( TraceLightPoint( bsp, node.Child( side ), start, middle, depth + 1, out color ) ) return true;
		for ( int i = 0; i < node.FaceCount; i++ )
		{
			int faceIndex = node.FirstFace + i;
			var face = bsp.Faces[faceIndex];
			var info = bsp.TexInfos[face.TextureInfo];
			if ( info.Special ) continue;
			var surface = _surfaces[faceIndex];
			float s = Vector3.Dot( middle, info.S ) + info.OffsetS - surface.UMin;
			float t = Vector3.Dot( middle, info.T ) + info.OffsetT - surface.VMin;
			int width = (int)surface.Size.x, height = (int)surface.Size.y;
			if ( width <= 0 || height <= 0 || s < 0 || t < 0 || s > (width - 1) * LuxelSize || t > (height - 1) * LuxelSize )
				continue;
			if ( face.LightmapOffset < 0 ) return true;
			int x = Math.Clamp( (int)MathF.Floor( s / LuxelSize + 0.5f ), 0, width - 1 );
			int y = Math.Clamp( (int)MathF.Floor( t / LuxelSize + 0.5f ), 0, height - 1 );
			Span<byte> styles = [face.Style0, face.Style1, face.Style2, face.Style3];
			int block = 0;
			foreach ( var style in styles )
			{
				if ( style == 255 ) break;
				int offset = checked( face.LightmapOffset + ((block++ * width * height + y * width + x) * 3) );
				if ( offset < 0 || offset > bsp.Lighting.Length - 3 )
					throw new InvalidDataException( $"{FilePath}: face {faceIndex} has invalid lightmap samples." );
				float scale = ProbeLightstyleScale( style );
				color += new Vector3( bsp.Lighting[offset], bsp.Lighting[offset + 1], bsp.Lighting[offset + 2] ) * scale;
			}
			return true;
		}
		return TraceLightPoint( bsp, node.Child( 1 - side ), middle, end, depth + 1, out color );
	}

	private static float ProbeLightstyleScale( int style )
	{
		if ( style >= DefaultLightstyles.Length ) return 1;
		var sequence = DefaultLightstyles[style];
		float sum = 0;
		foreach ( char c in sequence ) sum += (c - 'a') * 22f / 256;
		return sum / sequence.Length;
	}

	private static bool FindProbePosition( BspFile bsp, ref Vector3 position, Vector3 spacing )
	{
		if ( ProbeInOpenLeaf( bsp, position ) ) return true;
		var origin = position;
		for ( int axis = 0; axis < 3; axis++ )
			for ( int sign = -1; sign <= 1; sign += 2 )
			{
				var offset = Vector3.Zero;
				offset[axis] = spacing[axis] * 0.5f * sign;
				var candidate = origin + offset;
				if ( !ProbeInOpenLeaf( bsp, candidate ) ) continue;
				position = candidate;
				return true;
			}
		return false;
	}

	private static bool ProbeInOpenLeaf( BspFile bsp, Vector3 position )
	{
		int index = checked( (int)bsp.Models[0].HeadNode0 );
		int remaining = bsp.Nodes.Length;
		while ( index >= 0 )
		{
			if ( index >= bsp.Nodes.Length || remaining-- == 0 )
				throw new InvalidDataException( "Invalid GoldSrc BSP tree while locating a model light probe." );
			var node = bsp.Nodes[index];
			var plane = bsp.Planes[node.PlaneNum];
			index = node.Child( Vector3.Dot( position, plane.Normal ) < plane.Distance ? 1 : 0 );
		}
		int leaf = -1 - index;
		if ( leaf >= bsp.Leaves.Length )
			throw new InvalidDataException( "Invalid GoldSrc BSP leaf while locating a model light probe." );
		return !bsp.Leaves[leaf].Solid;
	}
}
