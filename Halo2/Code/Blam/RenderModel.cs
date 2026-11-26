using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Halo2.Blam;

/// <summary>
/// Parses a Halo 2 render_model (mode) tag.
/// References: Gravemind2401/Reclaimer - Halo2/render_model.cs, Halo2Common.cs
/// </summary>
public class RenderModelTag
{
	public List<BoundingBox> BoundingBoxes { get; } = new();
	public List<Region> Regions { get; } = new();
	public List<Section> Sections { get; } = new();
	public List<Node> Nodes { get; }
	public List<ShaderRef> Shaders { get; } = new();

	public RenderModelTag( MapFile map, TagEntry tag )
	{
		Nodes = ReadNodes( map, tag );
		if ( tag.DataSize == 0 ) return;
		var header = map.ReadSpan( map.VaToOffset( (int)tag.DataVirtualAddress ), checked((int)tag.DataSize) );
		ReadBlock( map, header[20..], 56, BoundingBoxes, row => new BoundingBox
		{
			XMin = Float( row, 0 ), XMax = Float( row, 4 ),
			YMin = Float( row, 8 ), YMax = Float( row, 12 ),
			ZMin = Float( row, 16 ), ZMax = Float( row, 20 ),
			UMin = Float( row, 24 ), UMax = Float( row, 28 ),
			VMin = Float( row, 32 ), VMax = Float( row, 36 )
		} );
		ReadBlock( map, header[28..], 16, Regions, row =>
		{
			var region = new Region { StringId = Int( row, 0 ) };
			ReadBlock( map, row[8..], 16, region.Permutations, p => new Permutation
			{
				StringId = Int( p, 0 ), PotatoSectionIndex = Short( p, 4 ), SuperLowSectionIndex = Short( p, 6 ),
				LowSectionIndex = Short( p, 8 ), MediumSectionIndex = Short( p, 10 ),
				HighSectionIndex = Short( p, 12 ), SuperHighSectionIndex = Short( p, 14 )
			} );
			return region;
		} );
		ReadBlock( map, header[36..], 92, Sections, row =>
		{
			var section = new Section
			{
				GeometryClassification = Short( row, 0 ),
				VertexCount = (ushort)Short( row, 4 ), FaceCount = (ushort)Short( row, 6 ),
				NodesPerVertex = row[20], TransparentNodesPerVertex = row[21],
				DataPointer = Int( row, 56 ), DataSize = Int( row, 60 ), HeaderSize = Int( row, 68 )
			};
			ReadBlock( map, row[72..], 16, section.Resources, r => new ResourceInfo
			{
				Type0 = Short( r, 4 ), Type1 = Short( r, 6 ), Size = Int( r, 8 ), Offset = Int( r, 12 )
			} );
			return section;
		} );
		ReadBlock( map, header[96..], 32, Shaders, row =>
		{
			int datum = Int( row, 12 );
			return new ShaderRef { TagIndex = datum & 0xffff, Datum = datum };
		} );
	}

	internal static List<Node> ReadNodes( MapFile map, TagEntry tag )
	{
		var nodes = new List<Node>();
		if ( tag.DataSize == 0 ) return nodes;
		if ( tag.DataSize < 80 ) throw new EndOfStreamException();
		var block = map.ReadSpan( map.VaToOffset( (int)tag.DataVirtualAddress ) + 72, 8 );
		ReadBlock( map, block, 96, nodes, row => new Node
		{
			StringId = Int( row, 0 ), ParentIndex = Short( row, 4 ),
			FirstChildIndex = Short( row, 6 ), NextSiblingIndex = Short( row, 8 ),
			PositionX = Float( row, 12 ), PositionY = Float( row, 16 ), PositionZ = Float( row, 20 ),
			RotationX = Float( row, 24 ), RotationY = Float( row, 28 ), RotationZ = Float( row, 32 ),
			RotationW = Float( row, 36 ), InverseScale = Float( row, 40 )
		} );
		return nodes;
	}

	static void ReadBlock<T>( MapFile map, ReadOnlySpan<byte> block, int stride, List<T> rows, Func<ReadOnlySpan<byte>, T> decode )
	{
		int count = Int( block, 0 ), pointer = Int( block, 4 );
		if ( count <= 0 || pointer == 0 ) return;
		var data = map.ReadSpan( map.VaToOffset( pointer ), checked(count * stride) );
		rows.Capacity = count;
		for ( int i = 0; i < count; i++ )
			rows.Add( decode( data.Slice( i * stride, stride ) ) );
	}

	static int Int( ReadOnlySpan<byte> row, int offset ) => BinaryPrimitives.ReadInt32LittleEndian( row[offset..] );
	static short Short( ReadOnlySpan<byte> row, int offset ) => BinaryPrimitives.ReadInt16LittleEndian( row[offset..] );
	static float Float( ReadOnlySpan<byte> row, int offset ) => BinaryPrimitives.ReadSingleLittleEndian( row[offset..] );
}

public class BoundingBox
{
	public float XMin, XMax, YMin, YMax, ZMin, ZMax;
	public float UMin, UMax, VMin, VMax;
}

public class Region
{
	public int StringId { get; set; }
	public List<Permutation> Permutations { get; } = new();
}

public class Permutation
{
	public int StringId { get; set; }
	public short PotatoSectionIndex { get; set; }
	public short SuperLowSectionIndex { get; set; }
	public short LowSectionIndex { get; set; }
	public short MediumSectionIndex { get; set; }
	public short HighSectionIndex { get; set; }
	public short SuperHighSectionIndex { get; set; }
}

public class Section
{
	public short GeometryClassification { get; set; }
	public ushort VertexCount { get; set; }
	public ushort FaceCount { get; set; }
	public byte NodesPerVertex { get; set; }
	public byte TransparentNodesPerVertex { get; set; }
	public int DataPointer { get; set; }
	public int DataSize { get; set; }
	public int HeaderSize { get; set; }
	public List<ResourceInfo> Resources { get; } = new();

	public int BaseAddress => DataSize - HeaderSize - 4;
}

public class ResourceInfo
{
	public short Type0 { get; set; }
	public short Type1 { get; set; }
	public int Size { get; set; }
	public int Offset { get; set; }
}

public class Node
{
	public int StringId { get; set; }
	public short ParentIndex { get; set; }
	public short FirstChildIndex { get; set; }
	public short NextSiblingIndex { get; set; }
	public float PositionX { get; set; }
	public float PositionY { get; set; }
	public float PositionZ { get; set; }
	public float RotationX { get; set; }
	public float RotationY { get; set; }
	public float RotationZ { get; set; }
	public float RotationW { get; set; }
	public float InverseScale { get; set; }
}

public class ShaderRef
{
	public int TagIndex { get; set; }
	public int Datum { get; set; }
}
