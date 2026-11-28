// Ported from OpenMagicka (MIT, https://github.com/vogster/OpenMagicka),
// src/OpenMagicka.Content/Xnb - XNA 3.1 (XNB v4) container and content reader.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace Magicka;

/// <summary>An entry of the type reader table: assembly-qualified reader name and its version.</summary>
public sealed record XnbTypeReader( string Name, int Version )
{
	/// <summary>
	/// Stable lookup key: type names without namespace, assembly or arity, generic arguments
	/// included recursively, e.g. <c>ListReader&lt;Vector3&gt;</c>.
	/// </summary>
	public string Key
	{
		get
		{
			var position = 0;
			var key = TypeKey( Name, ref position );
			var rest = Name.AsSpan( position ).TrimStart();
			return key is not null && (rest.IsEmpty || rest[0] == ',') ? key : Name;
		}
	}

	/// <summary>Parses "Namespace.Type`N[[Arg, Asm],[Arg, Asm]][]" starting at position.</summary>
	static string TypeKey( string name, ref int position )
	{
		var start = position;
		while ( position < name.Length && name[position] is not ('`' or '[' or ',' or ']') )
			position++;

		var fullName = name[start..position].Trim();
		if ( fullName.Length == 0 )
			return null;

		var key = fullName[(fullName.LastIndexOf( '.' ) + 1)..];

		if ( position < name.Length && name[position] == '`' )
		{
			var arityStart = ++position;
			while ( position < name.Length && char.IsAsciiDigit( name[position] ) )
				position++;

			if ( !int.TryParse( name.AsSpan( arityStart, position - arityStart ), out var arity ) || arity < 1 )
				return null;

			var arguments = GenericArguments( name, ref position );
			if ( arguments is null || arguments.Count != arity )
				return null;

			key += $"<{string.Join( ',', arguments )}>";
		}

		while ( position + 1 < name.Length && name[position] == '[' && name[position + 1] == ']' )
		{
			key += "[]";
			position += 2;
		}

		return key;
	}

	/// <summary>Parses "[[Type, Assembly],[Type, Assembly]]" and returns the argument keys.</summary>
	static List<string> GenericArguments( string name, ref int position )
	{
		if ( position >= name.Length || name[position] != '[' )
			return null;

		position++;
		var arguments = new List<string>();

		while ( true )
		{
			if ( position >= name.Length || name[position] != '[' )
				return null;

			position++;
			var argument = TypeKey( name, ref position );
			if ( argument is null || !SkipAssembly( name, ref position ) )
				return null;

			arguments.Add( argument );

			if ( position < name.Length && name[position] == ',' )
			{
				position++;
				continue;
			}

			if ( position < name.Length && name[position] == ']' )
			{
				position++;
				return arguments;
			}

			return null;
		}
	}

	/// <summary>Skips ", Assembly, Version=..." up to and including the closing ']' of an argument.</summary>
	static bool SkipAssembly( string name, ref int position )
	{
		var depth = 0;
		for ( ; position < name.Length; position++ )
		{
			switch ( name[position] )
			{
				case '[':
					depth++;
					break;

				case ']' when depth == 0:
					position++;
					return true;

				case ']':
					depth--;
					break;
			}
		}

		return false;
	}
}

/// <summary>An asset stored in another file; path relative to the referencing file's directory, without extension.</summary>
public sealed record ExternalReference( string Path );

/// <summary>0-based index into the shared resources of the same file.</summary>
public readonly record struct SharedResourceRef( int Index );

/// <summary>
/// Value of an object field that holds either an inline asset or a reference to another file
/// (XNA writes an ExternalReference object in place of the asset when it lives elsewhere).
/// </summary>
public readonly record struct ContentRef<T>( T Inline, ExternalReference External ) where T : class
{
	public bool IsNull => Inline is null && External is null;

	public static ContentRef<T> FromInline( T inline ) => new( inline, null );

	public static ContentRef<T> FromExternal( ExternalReference external ) => new( null, external );
}

public readonly struct Sphere( Vector3 center, float radius )
{
	public readonly Vector3 Center = center;
	public readonly float Radius = radius;
}

public readonly struct Box( Vector3 min, Vector3 max )
{
	public readonly Vector3 Min = min;
	public readonly Vector3 Max = max;
}

/// <summary>The file uses a content type that has no reader in the registry.</summary>
public sealed class XnbUnsupportedReaderException( string readerKey )
	: NotSupportedException( $"no reader for XNB content type '{readerKey}'" )
{
	public string ReaderKey { get; } = readerKey;
}

/// <summary>
/// An XNA 3.1 content file (XNB format version 4): header, LZX-decompressed body, type reader
/// table and shared resource count.
/// </summary>
public sealed class XnbFile
{
	public const byte SupportedVersion = 4;

	const int HeaderSize = 10;
	const int CompressedHeaderSize = 14;
	const byte CompressedFlag = 0x80;
	const int DefaultFrameSize = 0x8000;
	const int MaxDecompressedSize = 256 * 1024 * 1024;
	const int MaxTypeReaders = 1024;

	XnbFile( IReadOnlyList<XnbTypeReader> typeReaders, int sharedResourceCount, ReadOnlyMemory<byte> objects, byte[] body )
	{
		TypeReaders = typeReaders;
		SharedResourceCount = sharedResourceCount;
		Objects = objects;
		Body = body;
	}

	public IReadOnlyList<XnbTypeReader> TypeReaders { get; }
	public int SharedResourceCount { get; }

	/// <summary>Serialized primary object followed by the shared resources.</summary>
	public ReadOnlyMemory<byte> Objects { get; }

	public byte[] Body { get; }

	public static XnbFile Read( ReadOnlySpan<byte> file ) => Read( file, MaxDecompressedSize );

	/// <summary>Reads the container, decompressing at most <paramref name="maxBody"/> bytes of body.</summary>
	public static XnbFile Read( ReadOnlySpan<byte> file, int maxBody )
	{
		if ( file.Length < HeaderSize || !file[..3].SequenceEqual( "XNB"u8 ) )
			throw new InvalidDataException( "not an XNB file" );

		var version = file[4];
		if ( version != SupportedVersion )
			throw new InvalidDataException( $"XNB format version {version} is not supported; Magicka uses {SupportedVersion}" );

		var compressed = (file[5] & CompressedFlag) != 0;
		var fileSize = BinaryPrimitives.ReadInt32LittleEndian( file[6..] );
		if ( fileSize < HeaderSize )
			throw new InvalidDataException( $"implausible XNB size {fileSize}" );

		// A caller may pass only the head of a file to read its reader table; the rest is then missing.
		var available = Math.Min( fileSize, file.Length );

		var body = compressed
			? Decompress( file[..available], maxBody )
			: file[HeaderSize..Math.Min( available, HeaderSize + maxBody )].ToArray();

		return Parse( body );
	}

	static byte[] Decompress( ReadOnlySpan<byte> file, int maxBody )
	{
		if ( file.Length < CompressedHeaderSize )
			throw new InvalidDataException( "compressed XNB without a size field" );

		var size = BinaryPrimitives.ReadInt32LittleEndian( file[HeaderSize..] );
		if ( size is < 0 or > MaxDecompressedSize )
			throw new InvalidDataException( $"implausible decompressed XNB size {size}" );

		var wanted = Math.Min( size, maxBody );
		var output = new byte[wanted];
		var decoder = new LzxDecoder();
		var position = CompressedHeaderSize;
		var written = 0;

		while ( written < wanted )
		{
			if ( position + 2 > file.Length )
				break;

			var (frameSize, blockSize, headerLength) = ReadFrameHeader( file, position );
			position += headerLength;

			// A prefix read ends in the middle of the compressed stream; decode what is there.
			if ( blockSize == 0 || position + blockSize > file.Length )
				break;

			var count = Math.Min( frameSize, wanted - written );
			decoder.Decompress( file.Slice( position, blockSize ), output.AsSpan( written, count ) );

			position += blockSize;
			written += count;
		}

		return output;
	}

	/// <summary>A frame header is a big-endian block size, or 0xFF, frame size and block size.</summary>
	static (int FrameSize, int BlockSize, int HeaderLength) ReadFrameHeader( ReadOnlySpan<byte> file, int position )
	{
		if ( position + 2 > file.Length )
			throw new InvalidDataException( "XNB compressed data ends before its declared size" );

		if ( file[position] != 0xFF )
			return (DefaultFrameSize, (file[position] << 8) | file[position + 1], 2);

		if ( position + 5 > file.Length )
			throw new InvalidDataException( "truncated XNB frame header" );

		var frameSize = (file[position + 1] << 8) | file[position + 2];
		var blockSize = (file[position + 3] << 8) | file[position + 4];

		if ( frameSize is 0 or > DefaultFrameSize )
			throw new InvalidDataException( $"XNB frame size {frameSize} outside 1..{DefaultFrameSize}" );

		return (frameSize, blockSize, 5);
	}

	static XnbFile Parse( byte[] body )
	{
		using var stream = new MemoryStream( body, writable: false );
		using var reader = new BinaryReader( stream, Encoding.UTF8 );

		var count = reader.Read7BitEncodedInt();
		if ( count is < 0 or > MaxTypeReaders )
			throw new InvalidDataException( $"implausible type reader count {count}" );

		var readers = new XnbTypeReader[count];
		for ( var i = 0; i < count; i++ )
			readers[i] = new XnbTypeReader( reader.ReadString(), reader.ReadInt32() );

		var shared = reader.Read7BitEncodedInt();
		if ( shared < 0 )
			throw new InvalidDataException( $"negative shared resource count {shared}" );

		var start = (int)stream.Position;
		return new XnbFile( readers, shared, body.AsMemory( start ), body );
	}
}

/// <summary>
/// Reads the objects of an XNB file in the XNA 3.1 ContentReader encoding. Every read is
/// bounds-checked: malformed data raises <see cref="InvalidDataException"/>.
/// </summary>
public sealed class XnbContentReader
{
	public const int MaxDepth = 64;

	readonly BinaryReader reader;
	readonly IReadOnlyList<XnbTypeReader> table;
	readonly Func<XnbContentReader, object>[] resolved;
	int depth;

	internal XnbContentReader( XnbFile file )
	{
		reader = new BinaryReader( new MemoryStream( file.Body, writable: false ), Encoding.UTF8 );
		reader.BaseStream.Position = file.Body.Length - file.Objects.Length;
		table = file.TypeReaders;
		resolved = new Func<XnbContentReader, object>[table.Count];
		SharedResourceCount = file.SharedResourceCount;
	}

	public int SharedResourceCount { get; }

	public long Remaining => reader.BaseStream.Length - reader.BaseStream.Position;

	/// <summary>A type id (0 = null, otherwise 1-based index into the reader table) and that type's data.</summary>
	public object ReadObject()
	{
		var typeId = reader.Read7BitEncodedInt();
		if ( typeId == 0 )
			return null;

		if ( typeId < 0 || typeId > table.Count )
			throw new InvalidDataException( $"type id {typeId} outside the reader table of {table.Count}" );

		if ( depth >= MaxDepth )
			throw new InvalidDataException( $"objects nested deeper than {MaxDepth}" );

		var read = Resolve( typeId - 1 );
		depth++;
		try
		{
			return read( this );
		}
		finally
		{
			depth--;
		}
	}

	public T ReadObject<T>() where T : class => ReadObject() switch
	{
		null => null,
		T value => value,
		var other => throw new InvalidDataException( $"expected {typeof( T ).Name}, found {other.GetType().Name}" ),
	};

	/// <summary>An object that is either an inline <typeparamref name="T"/> or an external reference.</summary>
	public ContentRef<T> ReadContentRef<T>() where T : class => ReadObject() switch
	{
		null => default,
		T inline => ContentRef<T>.FromInline( inline ),
		ExternalReference external => ContentRef<T>.FromExternal( external ),
		var other => throw new InvalidDataException( $"expected {typeof( T ).Name} or a reference, found {other.GetType().Name}" ),
	};

	/// <summary>0 = null, otherwise a 1-based index into the file's shared resources.</summary>
	public SharedResourceRef? ReadSharedResourceRef()
	{
		var index = reader.Read7BitEncodedInt();
		if ( index == 0 )
			return null;

		if ( index < 0 || index > SharedResourceCount )
			throw new InvalidDataException( $"shared resource {index} outside the table of {SharedResourceCount}" );

		return new SharedResourceRef( index - 1 );
	}

	/// <summary>An inline path to another asset; an empty string means null.</summary>
	public ExternalReference ReadExternalReference()
	{
		var path = ReadString();
		return path.Length == 0 ? null : new ExternalReference( path );
	}

	/// <summary>A u32 element count, checked against the bytes left so it cannot force a huge allocation.</summary>
	public int ReadCount( int minBytesPerItem )
	{
		var count = reader.ReadUInt32();
		if ( count > int.MaxValue || (long)count * Math.Max( minBytesPerItem, 1 ) > Remaining )
			throw new InvalidDataException( $"count {count} exceeds the remaining {Remaining} bytes" );

		return (int)count;
	}

	/// <summary>A u32 byte count followed by that many bytes.</summary>
	public byte[] ReadSizedBytes() => ReadBytes( ReadCount( 1 ) );

	public byte[] ReadBytes( int count )
	{
		if ( count < 0 || count > Remaining )
			throw new InvalidDataException( $"{count} bytes requested, {Remaining} left" );

		return reader.ReadBytes( count );
	}

	public string ReadString() => reader.ReadString();

	public bool ReadBoolean() => reader.ReadByte() switch
	{
		0 => false,
		1 => true,
		var b => throw new InvalidDataException( $"boolean with value {b}" ),
	};

	public byte ReadByte() => reader.ReadByte();
	public short ReadInt16() => reader.ReadInt16();
	public ushort ReadUInt16() => reader.ReadUInt16();
	public int ReadInt32() => reader.ReadInt32();
	public uint ReadUInt32() => reader.ReadUInt32();
	public float ReadSingle() => reader.ReadSingle();

	public Vector2 ReadVector2() => new( ReadSingle(), ReadSingle() );
	public Vector3 ReadVector3() => new( ReadSingle(), ReadSingle(), ReadSingle() );
	public Vector4 ReadVector4() => new( ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle() );
	public Quaternion ReadQuaternion() => new( ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle() );

	/// <summary>16 floats M11..M44, XNA row-major order (row 4 is the translation).</summary>
	public Matrix4x4 ReadMatrix() => new(
		ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
		ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
		ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
		ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle() );

	public Sphere ReadBoundingSphere() => new( ReadVector3(), ReadSingle() );
	public Box ReadBoundingBox() => new( ReadVector3(), ReadVector3() );

	Func<XnbContentReader, object> Resolve( int index )
	{
		if ( resolved[index] is { } known )
			return known;

		var key = table[index].Key;
		if ( !XnbReaders.TryGet( key, out var found ) )
			throw new XnbUnsupportedReaderException( key );

		resolved[index] = found;
		return found;
	}
}

/// <summary>The objects of one XNB file: the primary object and the shared resources it points to.</summary>
public sealed class XnbContent
{
	XnbContent( object root, IReadOnlyList<object> sharedResources )
	{
		Root = root;
		SharedResources = sharedResources;
	}

	public object Root { get; }
	public IReadOnlyList<object> SharedResources { get; }

	public object Resolve( SharedResourceRef? reference )
	{
		if ( reference is not { } r )
			return null;

		if ( r.Index < 0 || r.Index >= SharedResources.Count )
			throw new InvalidDataException( $"shared resource {r.Index} outside {SharedResources.Count}" );

		return SharedResources[r.Index];
	}

	/// <summary>Reads every object of the file; throws when bytes are left over.</summary>
	public static XnbContent Load( XnbFile file )
	{
		var reader = new XnbContentReader( file );
		var root = reader.ReadObject();

		if ( reader.SharedResourceCount > reader.Remaining )
			throw new InvalidDataException( $"{reader.SharedResourceCount} shared resources declared, {reader.Remaining} bytes left" );

		var shared = new object[reader.SharedResourceCount];
		for ( var i = 0; i < shared.Length; i++ )
			shared[i] = reader.ReadObject();

		if ( reader.Remaining != 0 )
			throw new InvalidDataException( $"{reader.Remaining} bytes left after the last object" );

		return new XnbContent( root, shared );
	}

	/// <summary>Reads one XNB file from bytes.</summary>
	public static XnbContent Read( byte[] file ) => Load( XnbFile.Read( file ) );

	/// <summary>The primary object's type reader key, read without parsing the objects.</summary>
	public static string PeekRootReader( byte[] file )
	{
		var xnb = XnbFile.Read( file, 256 * 1024 );
		return xnb.TypeReaders.Count > 0 ? xnb.TypeReaders[0].Key : null;
	}
}
