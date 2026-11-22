using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace GoldSrc.Bsp;

internal enum LumpType
{
	Entities,
	Planes,
	Textures,
	Vertices,
	Visibility,
	Nodes,
	TexInfo,
	Faces,
	Lighting,
	ClipNodes,
	Leaves,
	MarkSurfaces,
	Edges,
	SurfEdges,
	Models,

	Count,
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct Plane
{
	public readonly Vector3 Normal;
	public readonly float Distance;
	public readonly int Type;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct TexInfo
{
	public readonly Vector3 S;
	public readonly float OffsetS;
	public readonly Vector3 T;
	public readonly float OffsetT;
	public readonly uint MipTex;
	public readonly uint Flags;

	/// <summary>
	/// TEX_SPECIAL - sky and liquid surfaces, which carry no lightmap.
	/// </summary>
	public bool Special => (Flags & 1) != 0;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct Face
{
	public readonly ushort Plane;
	public readonly ushort PlaneSide;
	public readonly int FirstEdge;
	public readonly ushort EdgeCount;
	public readonly ushort TextureInfo;

	public readonly byte Style0;
	public readonly byte Style1;
	public readonly byte Style2;
	public readonly byte Style3;

	public readonly int LightmapOffset;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct Edge
{
	public readonly ushort Vertex0;
	public readonly ushort Vertex1;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct BspModel
{
	public readonly Vector3 Mins;
	public readonly Vector3 Maxs;
	public readonly Vector3 Origin;

	public readonly uint HeadNode0;
	public readonly uint HeadNode1;
	public readonly uint HeadNode2;
	public readonly uint HeadNode3;

	public readonly int VisLeafCount;
	public readonly int FirstFace;
	public readonly int FaceCount;
}

/// <summary>
/// An interior node of the BSP tree. A child index is another node when it's positive and a leaf
/// when it's negative, encoded as -1 - leafIndex.
/// </summary>
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct Node
{
	public readonly int PlaneNum;

	public readonly short Child0;
	public readonly short Child1;

	public readonly short MinsX;
	public readonly short MinsY;
	public readonly short MinsZ;
	public readonly short MaxsX;
	public readonly short MaxsY;
	public readonly short MaxsZ;

	public readonly ushort FirstFace;
	public readonly ushort FaceCount;

	public short Child( int side ) => side == 0 ? Child0 : Child1;

	public static int LeafOf( short child ) => -1 - child;
}

/// <summary>
/// A BSP leaf - a convex cell of the tree. Solid leaves are the inside of the map's brushes;
/// everything else is space the player and the renderer can actually be in.
/// </summary>
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct Leaf
{
	/// <summary>CONTENTS_EMPTY (-1) down to CONTENTS_TRANSLUCENT (-15). Solid is -2.</summary>
	public readonly int Contents;
	public readonly int VisOffset;

	public readonly short MinsX;
	public readonly short MinsY;
	public readonly short MinsZ;
	public readonly short MaxsX;
	public readonly short MaxsY;
	public readonly short MaxsZ;

	/// <summary>Index into MarkSurfaces of the faces that bound this leaf.</summary>
	public readonly ushort FirstMarkSurface;
	public readonly ushort MarkSurfaceCount;

	/// <summary>
	/// Ambient *sound* levels - water, sky, slime and lava - not lighting. GoldSrc bakes no
	/// ambient light into the BSP at all, which is why it has to be derived from the lightmaps.
	/// </summary>
	public readonly byte AmbientWater;
	public readonly byte AmbientSky;
	public readonly byte AmbientSlime;
	public readonly byte AmbientLava;

	public const int ContentsSolid = -2;

	public bool Solid => Contents == ContentsSolid;

	/// <summary>
	/// Leaf bounds are stored as shorts, and qbsp writes an inverted box for leaves that ended up
	/// with no surfaces at all, so a caller has to check <see cref="HasVolume"/> before using it.
	/// </summary>
	public BBox Bounds => new( new Vector3( MinsX, MinsY, MinsZ ), new Vector3( MaxsX, MaxsY, MaxsZ ) );

	public bool HasVolume => MaxsX > MinsX && MaxsY > MinsY && MaxsZ > MinsZ;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct Lump
{
	public readonly int Offset;
	public readonly int Length;

	public Lump( int offset, int length )
	{
		Offset = offset;
		Length = length;
	}

	public static Lump ReadFromStream( BinaryReader reader ) => new( reader.ReadInt32(), reader.ReadInt32() );
}

/// <summary>
/// A GoldSrc (BSP version 30) map. The lump layout matches Quake's BSP29 exactly, the differences
/// are RGB lighting instead of greyscale, and textures carrying their own 256 colour palette
/// (or living in an external WAD3 named by worldspawn).
/// </summary>
internal class BspFile
{
	public const int Version = 30;

	public Plane[] Planes { get; private set; }
	public Vector3[] Vertices { get; private set; }
	public TexInfo[] TexInfos { get; private set; }
	public Face[] Faces { get; private set; }
	public Edge[] Edges { get; private set; }
	public int[] SurfEdges { get; private set; }
	public BspModel[] Models { get; private set; }
	public Leaf[] Leaves { get; private set; }
	public Node[] Nodes { get; private set; }

	/// <summary>Face indices, grouped per leaf by Leaf.FirstMarkSurface.</summary>
	public ushort[] MarkSurfaces { get; private set; }

	/// <summary>
	/// Raw RGB lightmap samples, three bytes per luxel.
	/// </summary>
	public byte[] Lighting { get; private set; }

	public BspTexture[] Textures { get; private set; }
	public List<Entity> Entities { get; private set; } = [];

	public BspFile( Stream stream )
	{
		stream.Seek( 0, SeekOrigin.Begin );
		using var reader = new BinaryReader( stream );

		var version = reader.ReadInt32();
		if ( version != Version )
			throw new ArgumentException( $"Not a GoldSrc BSP file (version {version}, expected {Version})" );

		var lumps = new Lump[(int)LumpType.Count];
		for ( var i = 0; i < lumps.Length; ++i )
		{
			lumps[i] = Lump.ReadFromStream( reader );
			var lump = lumps[i];
			if ( lump.Offset < 0 || lump.Length < 0 || lump.Offset > stream.Length - lump.Length )
				throw new InvalidDataException( $"GoldSrc BSP {(LumpType)i} lump is outside the file." );
		}

		Planes = ReadLump<Plane>( stream, lumps[(int)LumpType.Planes], LumpType.Planes );
		Vertices = ReadLump<Vector3>( stream, lumps[(int)LumpType.Vertices], LumpType.Vertices );
		TexInfos = ReadLump<TexInfo>( stream, lumps[(int)LumpType.TexInfo], LumpType.TexInfo );
		Faces = ReadLump<Face>( stream, lumps[(int)LumpType.Faces], LumpType.Faces );
		Edges = ReadLump<Edge>( stream, lumps[(int)LumpType.Edges], LumpType.Edges );
		SurfEdges = ReadLump<int>( stream, lumps[(int)LumpType.SurfEdges], LumpType.SurfEdges );
		Models = ReadLump<BspModel>( stream, lumps[(int)LumpType.Models], LumpType.Models );
		Leaves = ReadLump<Leaf>( stream, lumps[(int)LumpType.Leaves], LumpType.Leaves );
		Nodes = ReadLump<Node>( stream, lumps[(int)LumpType.Nodes], LumpType.Nodes );
		MarkSurfaces = ReadLump<ushort>( stream, lumps[(int)LumpType.MarkSurfaces], LumpType.MarkSurfaces );

		Lighting = ReadBytes( stream, lumps[(int)LumpType.Lighting] );

		ReadTextures( reader, lumps[(int)LumpType.Textures] );
		ParseEntities( Encoding.UTF8.GetString( ReadBytes( stream, lumps[(int)LumpType.Entities] ) ) );
	}

	/// <summary>
	/// The vertex a surfedge index refers to. Negative surfedges walk their edge backwards.
	/// </summary>
	public Vector3 VertexFromSurfEdge( int surfEdgeIndex )
	{
		var surfEdge = SurfEdges[surfEdgeIndex];
		return Vertices[surfEdge > 0 ? Edges[surfEdge].Vertex0 : Edges[-surfEdge].Vertex1];
	}

	private void ReadTextures( BinaryReader reader, Lump lump )
	{
		Textures = [];

		if ( lump.Length < sizeof( int ) )
			return;

		var stream = reader.BaseStream;
		stream.Seek( lump.Offset, SeekOrigin.Begin );

		var count = reader.ReadInt32();
		if ( count <= 0 )
			return;

		var offsets = new int[count];
		for ( var i = 0; i < count; ++i )
			offsets[i] = reader.ReadInt32();

		Textures = new BspTexture[count];

		for ( var i = 0; i < count; ++i )
		{
			if ( offsets[i] < 0 || lump.Offset + offsets[i] >= stream.Length )
			{
				Textures[i] = BspTexture.Missing( i );
				continue;
			}

			stream.Seek( lump.Offset + offsets[i], SeekOrigin.Begin );
			Textures[i] = MipTex.Read( reader, lump.Offset + offsets[i] ) ?? BspTexture.Missing( i );
		}
	}

	private static byte[] ReadBytes( Stream stream, Lump lump )
	{
		if ( lump.Length <= 0 )
			return [];

		var data = new byte[lump.Length];
		stream.Seek( lump.Offset, SeekOrigin.Begin );
		stream.ReadExactly( data );
		return data;
	}

	private static T[] ReadLump<T>( Stream stream, Lump lump, LumpType lumpType ) where T : unmanaged
	{
		var size = Unsafe.SizeOf<T>();
		if ( lump.Length % size != 0 )
			throw new InvalidDataException( $"GoldSrc BSP {lumpType} lump contains an incomplete {typeof( T ).Name}." );
		if ( lump.Length == 0 ) return [];
		var items = new T[lump.Length / size];
		using var reader = ByteStream.CreateReader( ReadBytes( stream, lump ) );
		for ( var i = 0; i < items.Length; ++i )
			items[i] = reader.Read<T>();
		return items;
	}

	private void ParseEntities( string content )
	{
		Dictionary<string, string> current = null;

		foreach ( var line in content.AsSpan().EnumerateLines() )
		{
			var trimmed = line.Trim();
			if ( trimmed.IsEmpty ) continue;

			if ( trimmed[0] == '{' )
			{
				current = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
				continue;
			}

			if ( trimmed[0] == '}' )
			{
				if ( current is not null )
					Entities.Add( new Entity( current ) );

				current = null;
				continue;
			}

			if ( current is null || trimmed[0] != '"' )
				continue;

			var keyEnd = trimmed[1..].IndexOf( '"' ) + 1;
			if ( keyEnd <= 0 )
				continue;

			var key = trimmed[1..keyEnd].ToString();
			var rest = trimmed[(keyEnd + 1)..].Trim();
			// Preserve assignment order, including repeated angle/angles keys.
			current.Remove( key );
			current[key] = rest.Trim( '"' ).ToString();
		}
	}
}
