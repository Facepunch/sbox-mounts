using System;
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
	public List<Node> Nodes { get; } = new();
	public List<ShaderRef> Shaders { get; } = new();

	public RenderModelTag( MapFile map, TagEntry tag )
	{
		if ( tag.DataSize == 0 )
			return;

		using var reader = map.GetTagDataReader( tag );

		// BoundingBoxes at +20 (TagBlock = count:4 + pointer:4)
		reader.BaseStream.Seek( 20, SeekOrigin.Begin );
		int bbCount = reader.ReadInt32();
		int bbPtr = reader.ReadInt32();

		// Regions at +28
		int regCount = reader.ReadInt32();
		int regPtr = reader.ReadInt32();

		// Sections at +36
		int secCount = reader.ReadInt32();
		int secPtr = reader.ReadInt32();

		// Nodes at +72
		reader.BaseStream.Seek( 72, SeekOrigin.Begin );
		int nodeCount = reader.ReadInt32();
		int nodePtr = reader.ReadInt32();

		// Shaders at +96
		reader.BaseStream.Seek( 96, SeekOrigin.Begin );
		int shaderCount = reader.ReadInt32();
		int shaderPtr = reader.ReadInt32();

		// Read bounding boxes (56 bytes each)
		if ( bbCount > 0 && bbPtr != 0 )
		{
			int bbOff = map.VaToOffset( bbPtr );
			for ( int i = 0; i < bbCount; i++ )
			{
				var bb = ReadBoundingBox( map, bbOff + i * 56 );
				BoundingBoxes.Add( bb );
			}
		}

		// Read regions (16 bytes each)
		if ( regCount > 0 && regPtr != 0 )
			ReadRegions( map, regPtr, regCount );

		// Read sections (92 bytes each)
		if ( secCount > 0 && secPtr != 0 )
			ReadSections( map, secPtr, secCount );

		// Read nodes (96 bytes each)
		if ( nodeCount > 0 && nodePtr != 0 )
			ReadNodes( map, nodePtr, nodeCount );

		// Read shader references (32 bytes each on MCC)
		if ( shaderCount > 0 && shaderPtr != 0 )
			ReadShaders( map, shaderPtr, shaderCount );
	}

	static BoundingBox ReadBoundingBox( MapFile map, int offset )
	{
		// BoundingBoxBlock: 56 bytes
		// +0: XBounds (float min, float max)
		// +8: YBounds, +16: ZBounds, +24: UBounds, +32: VBounds
		using var r = map.OpenReader( offset, 56 );

		return new BoundingBox
		{
			XMin = r.ReadSingle(), XMax = r.ReadSingle(),
			YMin = r.ReadSingle(), YMax = r.ReadSingle(),
			ZMin = r.ReadSingle(), ZMax = r.ReadSingle(),
			UMin = r.ReadSingle(), UMax = r.ReadSingle(),
			VMin = r.ReadSingle(), VMax = r.ReadSingle()
		};
	}

	void ReadRegions( MapFile map, int ptr, int count )
	{
		int baseOff = map.VaToOffset( ptr );
		for ( int i = 0; i < count; i++ )
		{
			int off = baseOff + i * 16;
			using var r = map.OpenReader( off, 16 );

			int nameStringId = r.ReadInt32();
			r.ReadInt32(); // padding

			// Permutations TagBlock at +8
			int permCount = r.ReadInt32();
			int permPtr = r.ReadInt32();

			var region = new Region { StringId = nameStringId };

			if ( permCount > 0 && permPtr != 0 )
			{
				int permOff = map.VaToOffset( permPtr );
				for ( int j = 0; j < permCount; j++ )
				{
					using var pr = map.OpenReader( permOff + j * 16, 16 );

					var perm = new Permutation
					{
						StringId = pr.ReadInt32(),
						PotatoSectionIndex = pr.ReadInt16(),
						SuperLowSectionIndex = pr.ReadInt16(),
						LowSectionIndex = pr.ReadInt16(),
						MediumSectionIndex = pr.ReadInt16(),
						HighSectionIndex = pr.ReadInt16(),
						SuperHighSectionIndex = pr.ReadInt16()
					};
					region.Permutations.Add( perm );
				}
			}

			Regions.Add( region );
		}
	}

	void ReadSections( MapFile map, int ptr, int count )
	{
		int baseOff = map.VaToOffset( ptr );
		for ( int i = 0; i < count; i++ )
		{
			int off = baseOff + i * 92;
			using var r = map.OpenReader( off, 92 );

			var section = new Section
			{
				GeometryClassification = r.ReadInt16(), // +0
			};
			r.ReadInt16(); // +2: padding

			section.VertexCount = r.ReadUInt16(); // +4
			section.FaceCount = r.ReadUInt16();   // +6

			// Skip to NodesPerVertex at +20
			r.BaseStream.Seek( 20, SeekOrigin.Begin );
			section.NodesPerVertex = r.ReadByte();

			// DataPointer at +56
			r.BaseStream.Seek( 56, SeekOrigin.Begin );
			section.DataPointer = r.ReadInt32();  // +56
			section.DataSize = r.ReadInt32();     // +60
			r.ReadInt32(); // +64
			section.HeaderSize = r.ReadInt32();   // +68

			// Resources TagBlock at +72
			int resCount = r.ReadInt32();
			int resPtr = r.ReadInt32();

			if ( resCount > 0 && resPtr != 0 )
			{
				int resOff = map.VaToOffset( resPtr );
				for ( int j = 0; j < resCount; j++ )
				{
					using var rr = map.OpenReader( resOff + j * 16, 16 );

					rr.ReadInt32(); // +0: unknown
					var res = new ResourceInfo
					{
						Type0 = rr.ReadInt16(), // +4
						Type1 = rr.ReadInt16(), // +6
						Size = rr.ReadInt32(),  // +8
						Offset = rr.ReadInt32() // +12
					};
					section.Resources.Add( res );
				}
			}

			Sections.Add( section );
		}
	}

	void ReadNodes( MapFile map, int ptr, int count )
	{
		int baseOff = map.VaToOffset( ptr );
		for ( int i = 0; i < count; i++ )
		{
			int off = baseOff + i * 96;
			using var r = map.OpenReader( off, 96 );

			var node = new Node
			{
				StringId = r.ReadInt32(),     // +0
				ParentIndex = r.ReadInt16(),  // +4
				FirstChildIndex = r.ReadInt16(), // +6
				NextSiblingIndex = r.ReadInt16(), // +8
			};
			r.ReadInt16(); // +10: padding

			// Position at +12 (float3)
			node.PositionX = r.ReadSingle();
			node.PositionY = r.ReadSingle();
			node.PositionZ = r.ReadSingle();

			// Rotation at +24 (quaternion XYZW)
			node.RotationX = r.ReadSingle();
			node.RotationY = r.ReadSingle();
			node.RotationZ = r.ReadSingle();
			node.RotationW = r.ReadSingle();

			// InverseScale at +40
			node.InverseScale = r.ReadSingle();

			Nodes.Add( node );
		}
	}

	void ReadShaders( MapFile map, int ptr, int count )
	{
		int baseOff = map.VaToOffset( ptr );
		// ShaderBlock for MCC (Halo2Xbox+): 32 bytes, TagReference at +8
		for ( int i = 0; i < count; i++ )
		{
			int off = baseOff + i * 32;
			using var r = map.OpenReader( off, 32 );

			r.BaseStream.Seek( 8, SeekOrigin.Begin );
			int classId = r.ReadInt32();   // TagReference classId
			int tagDatum = r.ReadInt32();  // TagReference tagId (datum)

			int tagIndex = tagDatum & 0xFFFF;
			Shaders.Add( new ShaderRef { TagIndex = tagIndex, Datum = tagDatum } );
		}
	}
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
