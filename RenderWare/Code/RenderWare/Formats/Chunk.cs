using System;

namespace Renderware;

/// <summary>
/// RenderWare binary stream chunk ids we understand.
/// https://gtamods.com/wiki/RenderWare_binary_stream_file
/// </summary>
public enum ChunkType : uint
{
	Struct = 0x01,
	String = 0x02,
	Extension = 0x03,
	Texture = 0x06,
	Material = 0x07,
	MaterialList = 0x08,
	FrameList = 0x0E,
	Geometry = 0x0F,
	Clump = 0x10,
	Atomic = 0x14,
	TextureNative = 0x15,
	TextureDictionary = 0x16,
	GeometryList = 0x1A,

	// plugins
	Skin = 0x116,
	HAnim = 0x11E,
	BinMesh = 0x50E,
	Effect2D = 0x253F2F8,
	FrameName = 0x253F2FE,
}

public readonly struct ChunkHeader
{
	public const int Size = 12;

	public readonly ChunkType Type;
	public readonly uint Length;
	public readonly uint Version;

	public ChunkHeader( BinaryReader br )
	{
		Type = (ChunkType)br.ReadUInt32();
		Length = br.ReadUInt32();
		Version = br.ReadUInt32();
	}

	/// <summary>
	/// The RenderWare library version this chunk was written with, e.g. 0x34003 for 3.4.0.3.
	/// </summary>
	public uint LibraryVersion => (Version & 0xFFFF0000) != 0
		? ((Version >> 14) & 0x3FF00) + 0x30000 | ((Version >> 16) & 0x3F)
		: Version << 8;

	public override string ToString() => $"{Type} ({Length} bytes)";
}

/// <summary>
/// A parsed chunk. Types we don't care about stay as a plain <see cref="Chunk"/> with just their header.
/// </summary>
public class Chunk( ChunkHeader header )
{
	public ChunkHeader Header { get; } = header;

	public override string ToString() => Header.ToString();
}

public static class Chunks
{
	/// <summary>
	/// Reads the root chunk of a RenderWare file (a clump for .dff, a texture dictionary for .txd), skipping anything
	/// before it - San Andreas' animated models start with a UV animation dictionary.
	/// </summary>
	public static T ReadRoot<T>( Stream stream ) where T : Chunk
	{
		using var br = new BinaryReader( stream, System.Text.Encoding.ASCII, leaveOpen: true );
		while ( Read( br ) is { } chunk )
		{
			if ( chunk is T root )
				return root;
		}

		throw new InvalidDataException( $"No {typeof( T ).Name} chunk" );
	}

	/// <summary>
	/// Reads the next chunk, parsing it if it's a type we know. Null at the end of the data.
	/// Each chunk is parsed from its own buffer, so a bad chunk can't throw the rest of the stream off.
	/// </summary>
	public static Chunk Read( BinaryReader br, Chunk parent = null )
	{
		if ( br.BaseStream.Length - br.BaseStream.Position < ChunkHeader.Size )
			return null;

		var header = new ChunkHeader( br );
		if ( header.Length == 0 )
			return new Chunk( header );

		var bytes = br.ReadBytes( (int)Math.Min( header.Length, br.BaseStream.Length - br.BaseStream.Position ) );
		using var r = new BinaryReader( new MemoryStream( bytes, false ) );

		return header.Type switch
		{
			ChunkType.Clump => new Clump( r, header ),
			ChunkType.String => new StringChunk( r, header ),
			ChunkType.Atomic => new Atomic( r, header ),
			ChunkType.FrameList => new FrameList( r, header ),
			ChunkType.GeometryList => new GeometryList( r, header ),
			ChunkType.Geometry => new Geometry( r, header ),
			ChunkType.MaterialList => new MaterialList( r, header ),
			ChunkType.Material => new MaterialInfo( r, header ),
			ChunkType.Texture => new TextureInfo( r, header ),
			ChunkType.TextureDictionary => new TextureDictionary( r, header ),
			ChunkType.BinMesh => new MaterialSplitList( r, header ),
			ChunkType.Skin => new Skin( r, header, parent ),
			ChunkType.HAnim => new HAnim( r, header ),
			ChunkType.FrameName => new FrameName( r, header ),
			ChunkType.Effect2D => new Effect2DList( r, header ),
			_ => new Chunk( header ),
		};
	}

	public static T Read<T>( BinaryReader br, Chunk parent = null ) where T : Chunk => Read( br, parent ) as T;

	/// <summary>
	/// Reads the extension chunk that ends most sections, returning the plugin chunks inside it.
	/// </summary>
	public static List<Chunk> ReadExtensions( BinaryReader br, Chunk parent )
	{
		var list = new List<Chunk>();
		if ( br.BaseStream.Length - br.BaseStream.Position < ChunkHeader.Size )
			return list;

		var header = new ChunkHeader( br );
		if ( header.Type != ChunkType.Extension )
			return list;

		long end = br.BaseStream.Position + header.Length;
		while ( br.BaseStream.Position + ChunkHeader.Size <= end && Read( br, parent ) is { } chunk )
			list.Add( chunk );

		return list;
	}

	/// <summary>
	/// Every section starts with a struct chunk holding its fields - skip its header.
	/// </summary>
	public static ChunkHeader ReadStruct( BinaryReader br )
	{
		var header = new ChunkHeader( br );
		if ( header.Type != ChunkType.Struct )
			throw new InvalidDataException( $"Expected a struct chunk, got {header.Type}" );
		return header;
	}
}

/// <summary>
/// The top level container of a .dff - frames, geometries and the atomics that tie them together.
/// </summary>
public class Clump : Chunk
{
	public List<Chunk> Children { get; } = new();

	public Clump( BinaryReader br, ChunkHeader header ) : base( header )
	{
		// a few of GTA III's unused peds (chunky, dealer...) are cut off part way through their last atomics - keep
		// everything up to the cut
		try
		{
			while ( Chunks.Read( br ) is { } child )
				Children.Add( child );
		}
		catch ( EndOfStreamException )
		{
		}
	}
}

public class StringChunk : Chunk
{
	public string Value { get; }

	public StringChunk( BinaryReader br, ChunkHeader header ) : base( header )
	{
		Value = new string( br.ReadChars( (int)header.Length ) ).TrimEnd( '\0' );
	}
}

/// <summary>
/// Puts a geometry on a frame.
/// </summary>
public class Atomic : Chunk
{
	[Flags]
	public enum AtomicFlags
	{
		CollisionTest = 0x01,
		Render = 0x04,
	}

	public int FrameIndex { get; }
	public int GeometryIndex { get; internal set; }
	public AtomicFlags Flags { get; }

	/// <summary>
	/// Very early files (RenderWare 3.0 - some of GTA III's generic peds) have no geometry list: each atomic carries
	/// its own geometry, and its struct has no geometry index.
	/// </summary>
	public Geometry EmbeddedGeometry { get; }

	public Atomic( BinaryReader br, ChunkHeader header ) : base( header )
	{
		var data = Chunks.ReadStruct( br );
		FrameIndex = br.ReadInt32();

		if ( data.Length < 16 )
		{
			Flags = (AtomicFlags)br.ReadUInt32();
			br.BaseStream.Position += data.Length - 8;
			EmbeddedGeometry = Chunks.Read<Geometry>( br );
			return;
		}

		GeometryIndex = br.ReadInt32();
		Flags = (AtomicFlags)br.ReadUInt32();
	}
}
