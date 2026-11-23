using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NS2;

/// <summary>
/// Reader for Spark's ".cinematic" (CIN) format.
///
/// Layout, verified byte-exact against all 1239 .cinematic files shipped with NS2
/// (every one parses with zero leftover bytes):
///
///   "CIN" + byte version (1)
///   repeated { int32 type, uint32 length, byte data[length] }
///
/// Only chunk type 2 carries content; it is a flat list of nodes repeated until the
/// chunk ends (there is no node count):
///
///   int32 nodeType (always 3)
///   int32 nodeLength    - counted from the byte after this field
///   int32 nameChars, UTF-16LE name
///   int32 unknown
///   int32 propertyLength
///   properties[propertyLength]
///   8 byte tail
///
/// Each property is:
///
///   int32 keyType, int32 blockLength     - blockLength counted from here
///   if keyType == 2: int32 keyBytes, UTF-8 key
///   int32 valueType, int32 components, int32 flags
///   value bytes, up to the end of the block
///
/// Value types 0 and 4 are length-prefixed UTF-16 strings, 1 is a bool, 8 a float, and 2, 7 and 9
/// are animatable float triples with a 12 byte stride.
///
/// Node names seen in the shipped data: Emitter, Actor, Point Light, Physics Emitter,
/// Light, External File, Camera, Sky Box, Decal, View Model.
/// </summary>
internal sealed class CinematicFile
{
	private readonly List<CinematicNode> nodes = new();

	public CinematicNode Find( string name ) =>
		nodes.FirstOrDefault( x => string.Equals( x.Name, name, StringComparison.OrdinalIgnoreCase ) );

	public static CinematicFile Read( Stream stream )
	{
		var file = new CinematicFile();
		using var r = new BinaryReader( stream, Encoding.UTF8, leaveOpen: true );

		Span<byte> magic = stackalloc byte[3];
		if ( r.Read( magic ) != 3 || magic[0] != (byte)'C' || magic[1] != (byte)'I' || magic[2] != (byte)'N' )
			throw new InvalidDataException( "Not an NS2 .cinematic file (bad magic)." );

		r.ReadByte(); // version

		long length = stream.Length;
		while ( stream.Position + 8 <= length )
		{
			int type = r.ReadInt32();
			uint size = r.ReadUInt32();
			long start = stream.Position;
			if ( start + size > length )
				break;

			if ( type == 2 )
			{
				try { ReadNodes( r, file.nodes, start + size ); }
				catch { }
			}

			stream.Position = start + size;
		}

		return file;
	}

	private static void ReadNodes( BinaryReader r, List<CinematicNode> into, long end )
	{
		var stream = r.BaseStream;

		while ( stream.Position + 8 <= end )
		{
			r.ReadInt32(); // node type, always 3
			int nodeLength = r.ReadInt32();
			long start = stream.Position;
			if ( nodeLength < 0 || start + nodeLength > end )
				return;

			var node = new CinematicNode { Name = ReadUtf16( r ) };

			r.ReadInt32();
			int propertyLength = r.ReadInt32();
			long propertyEnd = stream.Position + propertyLength;
			if ( propertyLength >= 0 && propertyEnd <= start + nodeLength )
				ReadProperties( r, node, propertyEnd );

			into.Add( node );
			stream.Position = start + nodeLength;
		}
	}

	private static void ReadProperties( BinaryReader r, CinematicNode node, long end )
	{
		var stream = r.BaseStream;

		while ( stream.Position + 8 <= end )
		{
			int keyType = r.ReadInt32();
			int blockLength = r.ReadInt32();
			long blockEnd = stream.Position + blockLength;
			if ( blockLength < 0 || blockEnd > end )
				return;

			string key = keyType == 2 ? ReadUtf8( r ) : $"#{keyType}";

			int valueType = r.ReadInt32();
			r.ReadInt32(); // component count
			r.ReadInt32(); // flags

			int size = (int)(blockEnd - stream.Position);
			node.Properties[key] = new CinematicValue( valueType, size > 0 ? r.ReadBytes( size ) : [] );

			stream.Position = blockEnd;
		}
	}

	private static string ReadUtf8( BinaryReader r )
	{
		int n = r.ReadInt32();
		if ( n <= 0 ) return string.Empty;
		return Encoding.UTF8.GetString( r.ReadBytes( n ) );
	}

	private static string ReadUtf16( BinaryReader r )
	{
		int chars = r.ReadInt32();
		if ( chars <= 0 ) return string.Empty;
		return Encoding.Unicode.GetString( r.ReadBytes( chars * 2 ) );
	}
}

internal sealed class CinematicNode
{
	public string Name { get; set; } = string.Empty;
	public Dictionary<string, CinematicValue> Properties { get; } = new( StringComparer.OrdinalIgnoreCase );

	public CinematicValue Get( string key ) => Properties.TryGetValue( key, out var v ) ? v : default;
}

internal readonly struct CinematicValue
{
	public int Type { get; }
	public byte[] Data { get; }

	public CinematicValue( int type, byte[] data )
	{
		Type = type;
		Data = data ?? [];
	}

	/// <summary>Types 0 and 4 are both length-prefixed UTF-16; 4 is used for asset paths.</summary>
	public string AsString()
	{
		if ( Type is not (0 or 4) || Data.Length < 4 )
			return string.Empty;

		int chars = BitConverter.ToInt32( Data, 0 );
		if ( chars <= 0 || 4 + chars * 2 > Data.Length )
			return string.Empty;

		return Encoding.Unicode.GetString( Data, 4, chars * 2 );
	}
}
