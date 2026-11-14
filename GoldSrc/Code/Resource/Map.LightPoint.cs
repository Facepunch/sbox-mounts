using System;
using System.IO;
using GoldSrc.Bsp;

namespace GoldSrc;

partial class MapLoader
{
	// Sample the floor, not the mean lighting of a room's walls.
	private void SampleFloorLight( BspFile bsp, Vector3 position, Span<byte> samples )
	{
		for ( int slot = 0; slot < 4; slot++ ) samples[slot * 4 + 3] = 255;
		if ( bsp.Lighting.Length == 0 ) return;
		var start = position + Vector3.Up * 8;
		TraceLightPoint( bsp, checked((int)bsp.Models[0].HeadNode0), start, start + Vector3.Down * 2048, 0, samples );
	}

	private bool TraceLightPoint( BspFile bsp, int index, Vector3 start, Vector3 end, int depth, Span<byte> samples )
	{
		if ( index < 0 ) return false;
		if ( index >= bsp.Nodes.Length || depth >= 128 )
			throw new InvalidDataException( $"{FilePath}: invalid BSP tree while sampling model lighting." );
		var node = bsp.Nodes[index];
		var plane = bsp.Planes[node.PlaneNum];
		float front = Vector3.Dot( start, plane.Normal ) - plane.Distance;
		float back = Vector3.Dot( end, plane.Normal ) - plane.Distance;
		int side = front < 0 ? 1 : 0;
		if ( (back < 0) == (front < 0) )
			return TraceLightPoint( bsp, node.Child( side ), start, end, depth + 1, samples );
		var middle = start + (end - start) * (front / (front - back));
		if ( TraceLightPoint( bsp, node.Child( side ), start, middle, depth + 1, samples ) ) return true;
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
			int x = (int)s / LuxelSize;
			int y = (int)t / LuxelSize;
			Span<byte> styles = [face.Style0, face.Style1, face.Style2, face.Style3];
			int block = 0;
			foreach ( var style in styles )
			{
				if ( style == 255 ) break;
				int offset = checked(face.LightmapOffset + ((block * width * height + y * width + x) * 3));
				if ( offset < 0 || offset > bsp.Lighting.Length - 3 )
					throw new InvalidDataException( $"{FilePath}: face {faceIndex} has invalid lightmap samples." );
				bsp.Lighting.AsSpan( offset, 3 ).CopyTo( samples.Slice( block * 4, 3 ) );
				samples[block * 4 + 3] = style;
				block++;
			}
			return true;
		}
		return TraceLightPoint( bsp, node.Child( 1 - side ), middle, end, depth + 1, samples );
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
		int index = checked((int)bsp.Models[0].HeadNode0);
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
