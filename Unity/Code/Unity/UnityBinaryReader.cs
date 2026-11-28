using System.Buffers.Binary;
using System.Text;

namespace UnityMount;

/// <summary>
/// Binary reader helpers matching Unity's SerializedFile alignment and endianness conventions.
/// </summary>
public sealed class UnityBinaryReader : IDisposable
{
	readonly Stream _stream;
	readonly bool _leaveOpen;
	readonly byte[] _buf = new byte[16];

	public bool IsBigEndian { get; set; }
	public Stream BaseStream => _stream;
	public long Position { get => _stream.Position; set => _stream.Position = value; }
	public long Length => _stream.Length;

	public UnityBinaryReader( Stream stream, bool bigEndian = false, bool leaveOpen = false )
	{
		_stream = stream;
		IsBigEndian = bigEndian;
		_leaveOpen = leaveOpen;
	}

	public void Dispose()
	{
		if ( !_leaveOpen ) _stream.Dispose();
	}

	public void Align( int alignment = 4 )
	{
		long mod = _stream.Position % alignment;
		if ( mod != 0 ) _stream.Position += alignment - mod;
	}

	public void Skip( long count )
	{
		if ( count > 0 ) _stream.Position += count;
	}

	public byte ReadByte()
	{
		int b = _stream.ReadByte();
		if ( b < 0 ) throw new EndOfStreamException();
		return (byte)b;
	}

	public bool ReadBool() => ReadByte() != 0;

	public byte[] ReadBytes( int count )
	{
		if ( count <= 0 ) return [];
		var data = new byte[count];
		_stream.ReadExactly( data );
		return data;
	}

	public short ReadInt16()
	{
		_stream.ReadExactly( _buf.AsSpan( 0, 2 ) );
		return IsBigEndian ? BinaryPrimitives.ReadInt16BigEndian( _buf ) : BinaryPrimitives.ReadInt16LittleEndian( _buf );
	}

	public ushort ReadUInt16()
	{
		_stream.ReadExactly( _buf.AsSpan( 0, 2 ) );
		return IsBigEndian ? BinaryPrimitives.ReadUInt16BigEndian( _buf ) : BinaryPrimitives.ReadUInt16LittleEndian( _buf );
	}

	public int ReadInt32()
	{
		_stream.ReadExactly( _buf.AsSpan( 0, 4 ) );
		return IsBigEndian ? BinaryPrimitives.ReadInt32BigEndian( _buf ) : BinaryPrimitives.ReadInt32LittleEndian( _buf );
	}

	public uint ReadUInt32()
	{
		_stream.ReadExactly( _buf.AsSpan( 0, 4 ) );
		return IsBigEndian ? BinaryPrimitives.ReadUInt32BigEndian( _buf ) : BinaryPrimitives.ReadUInt32LittleEndian( _buf );
	}

	public long ReadInt64()
	{
		_stream.ReadExactly( _buf.AsSpan( 0, 8 ) );
		return IsBigEndian ? BinaryPrimitives.ReadInt64BigEndian( _buf ) : BinaryPrimitives.ReadInt64LittleEndian( _buf );
	}

	public ulong ReadUInt64()
	{
		_stream.ReadExactly( _buf.AsSpan( 0, 8 ) );
		return IsBigEndian ? BinaryPrimitives.ReadUInt64BigEndian( _buf ) : BinaryPrimitives.ReadUInt64LittleEndian( _buf );
	}

	public float ReadSingle()
	{
		_stream.ReadExactly( _buf.AsSpan( 0, 4 ) );
		return IsBigEndian ? BinaryPrimitives.ReadSingleBigEndian( _buf ) : BinaryPrimitives.ReadSingleLittleEndian( _buf );
	}

	public uint ReadBEUInt32()
	{
		_stream.ReadExactly( _buf.AsSpan( 0, 4 ) );
		return BinaryPrimitives.ReadUInt32BigEndian( _buf );
	}

	public ulong ReadBEUInt64()
	{
		_stream.ReadExactly( _buf.AsSpan( 0, 8 ) );
		return BinaryPrimitives.ReadUInt64BigEndian( _buf );
	}

	public string ReadCString( int maxLen = 4096 )
	{
		var bytes = new List<byte>( 64 );
		while ( bytes.Count < maxLen )
		{
			int b = _stream.ReadByte();
			if ( b <= 0 ) break;
			bytes.Add( (byte)b );
		}
		return Encoding.UTF8.GetString( bytes.ToArray() );
	}

	public string ReadAlignedString()
	{
		int len = ReadInt32();
		if ( len <= 0 ) return string.Empty;
		if ( len > 16 * 1024 * 1024 )
			throw new InvalidDataException( $"Invalid Unity string length: {len}" );
		var bytes = ReadBytes( len );
		Align( 4 );
		return Encoding.UTF8.GetString( bytes );
	}

	public PPtr ReadPPtr() => new( ReadInt32(), ReadInt64() );

	public Vector2 ReadVector2() => new( ReadSingle(), ReadSingle() );
	public Vector3 ReadVector3() => new( ReadSingle(), ReadSingle(), ReadSingle() );
	public Vector4 ReadVector4() => new( ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle() );
	public Color ReadColor() => new( ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle() );

	public StreamingInfo ReadStreamingInfo()
	{
		ulong offset = ReadUInt64();
		uint size = ReadUInt32();
		string path = ReadAlignedString();
		return new StreamingInfo( offset, size, path );
	}
}
