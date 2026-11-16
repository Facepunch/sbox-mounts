using System;
using System.Buffers.Binary;
using System.Text;

namespace Doom.Wad;

public enum MapFormat
{
	/// <summary>Vanilla Doom binary format.</summary>
	Doom,

	/// <summary>Hexen binary format (has a BEHAVIOR lump). Different THINGS and LINEDEFS records.</summary>
	Hexen,

	/// <summary>Text based UDMF (TEXTMAP).</summary>
	Udmf,
}

/// <summary>
/// One level: a marker lump (E1M1, MAP01) and the data lumps that follow it.
/// Records are parsed on first access and cached. Only <see cref="MapFormat.Doom"/> records are decoded.
/// </summary>
public sealed class MapData
{
	private delegate T Parser<T>( ReadOnlySpan<byte> record );

	private Thing[] _things;
	private Linedef[] _linedefs;
	private Vertex[] _vertexes;
	private Sidedef[] _sidedefs;
	private Sector[] _sectors;
	private Seg[] _segs;
	private Subsector[] _subsectors;
	private Node[] _nodes;

	internal MapData( Lump marker, IReadOnlyDictionary<string, Lump> lumps )
	{
		Marker = marker;
		Lumps = lumps;
		Format = lumps.ContainsKey( "TEXTMAP" ) ? MapFormat.Udmf
			: lumps.ContainsKey( "BEHAVIOR" ) ? MapFormat.Hexen
			: MapFormat.Doom;
	}

	public string Name => Marker.Name;

	public Lump Marker { get; }

	public WadFile Wad => Marker.Wad;

	public MapFormat Format { get; }

	/// <summary>The raw data lumps of this map by name (THINGS, LINEDEFS, SIDEDEFS, NODES ...).</summary>
	public IReadOnlyDictionary<string, Lump> Lumps { get; }

	public IReadOnlyList<Thing> Things => _things ??= Read( "THINGS", 10, ParseThing );
	public IReadOnlyList<Linedef> Linedefs => _linedefs ??= Read( "LINEDEFS", 14, ParseLinedef );
	public IReadOnlyList<Vertex> Vertexes => _vertexes ??= Read( "VERTEXES", 4, ParseVertex );
	public IReadOnlyList<Sidedef> Sidedefs => _sidedefs ??= Read( "SIDEDEFS", 30, ParseSidedef );
	public IReadOnlyList<Sector> Sectors => _sectors ??= Read( "SECTORS", 26, ParseSector );
	public IReadOnlyList<Seg> Segs => _segs ??= Read( "SEGS", 12, ParseSeg, optional: true );
	public IReadOnlyList<Subsector> Subsectors => _subsectors ??= Read( "SSECTORS", 4, ParseSubsector, optional: true );
	public IReadOnlyList<Node> Nodes => _nodes ??= Read( "NODES", 28, ParseNode, optional: true );

	public override string ToString() => Name;

	private T[] Read<T>( string lumpName, int recordSize, Parser<T> parse, bool optional = false )
	{
		if ( Format != MapFormat.Doom )
			throw new NotSupportedException( $"{Name} is a {Format} format map." );

		if ( !Lumps.TryGetValue( lumpName, out var lump ) )
		{
			if ( optional ) return [];
			throw new InvalidDataException( $"{Name} has no {lumpName} lump." );
		}

		// Trailing partial records are ignored, as vanilla does.
		var data = lump.ReadAllBytes();
		var result = new T[data.Length / recordSize];
		for ( int i = 0; i < result.Length; i++ )
			result[i] = parse( data.AsSpan( i * recordSize, recordSize ) );
		return result;
	}

	/// <summary>Texture and flat names are 8 ASCII bytes, null padded.</summary>
	internal static string ParseName( ReadOnlySpan<byte> raw )
	{
		int len = raw.IndexOf( (byte)0 );
		if ( len < 0 ) len = raw.Length;
		return Encoding.ASCII.GetString( raw[..len] ).Trim().ToUpperInvariant();
	}

	private static short I16( ReadOnlySpan<byte> r, int at ) => BinaryPrimitives.ReadInt16LittleEndian( r[at..] );
	private static ushort U16( ReadOnlySpan<byte> r, int at ) => BinaryPrimitives.ReadUInt16LittleEndian( r[at..] );

	private static Vertex ParseVertex( ReadOnlySpan<byte> r ) => new( I16( r, 0 ), I16( r, 2 ) );

	private static Linedef ParseLinedef( ReadOnlySpan<byte> r ) => new(
		U16( r, 0 ), U16( r, 2 ), (LinedefFlags)U16( r, 4 ), U16( r, 6 ), U16( r, 8 ), U16( r, 10 ), U16( r, 12 ) );

	private static Thing ParseThing( ReadOnlySpan<byte> r ) => new(
		I16( r, 0 ), I16( r, 2 ), I16( r, 4 ), U16( r, 6 ), (ThingFlags)U16( r, 8 ) );

	// x, y, upper[8], lower[8], middle[8], sector
	private static Sidedef ParseSidedef( ReadOnlySpan<byte> r ) => new(
		I16( r, 0 ), I16( r, 2 ), ParseName( r.Slice( 4, 8 ) ), ParseName( r.Slice( 12, 8 ) ), ParseName( r.Slice( 20, 8 ) ), U16( r, 28 ) );

	// floor, ceiling, floor flat[8], ceiling flat[8], light, special, tag
	private static Sector ParseSector( ReadOnlySpan<byte> r ) => new(
		I16( r, 0 ), I16( r, 2 ), ParseName( r.Slice( 4, 8 ) ), ParseName( r.Slice( 12, 8 ) ), I16( r, 20 ), U16( r, 22 ), U16( r, 24 ) );

	// start, end, angle, linedef, direction (0 = same as linedef), offset
	private static Seg ParseSeg( ReadOnlySpan<byte> r ) => new(
		U16( r, 0 ), U16( r, 2 ), I16( r, 4 ), U16( r, 6 ), I16( r, 8 ) != 0, I16( r, 10 ) );

	private static Subsector ParseSubsector( ReadOnlySpan<byte> r ) => new( U16( r, 0 ), U16( r, 2 ) );

	// x, y, dx, dy, right bbox[4], left bbox[4], right child, left child. The bounding boxes are skipped.
	private static Node ParseNode( ReadOnlySpan<byte> r ) => new( I16( r, 0 ), I16( r, 2 ), I16( r, 4 ), I16( r, 6 ), U16( r, 24 ), U16( r, 26 ) );
}
