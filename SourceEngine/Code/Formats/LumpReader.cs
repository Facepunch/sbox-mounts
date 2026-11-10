using System;
using System.Runtime.CompilerServices;

namespace Source1;

/// <summary>
/// Reads packed, unmanaged records from Source file lumps. Records are copied out through
/// <see cref="ByteStream"/>, so every record type must be unmanaged: fixed-size arrays are
/// spelled out as fields or padding structs rather than marshalled arrays.
/// </summary>
internal static class LumpReader<TLump> where TLump : unmanaged
{
	[Sandbox.SkipHotload]
	private static readonly int Size = Unsafe.SizeOf<TLump>();

	public static void ValidateRange( Stream stream, long offset, int count )
	{
		if ( count < 0 || count > int.MaxValue / Size || offset < 0
			|| (stream.CanSeek && (offset > stream.Length || (long)count * Size > stream.Length - offset)) )
			throw new InvalidDataException( $"Invalid {typeof( TLump ).Name} range (offset {offset}, count {count})." );
	}

	private static TLump[] Read( ReadOnlySpan<byte> src, int count )
	{
		var array = new TLump[count];
		using var reader = ByteStream.CreateReader( src );
		for ( var i = 0; i < array.Length; ++i )
			array[i] = reader.Read<TLump>();
		return array;
	}

	public static TLump[] ReadLump( byte[] src, int offset, int length )
	{
		ArgumentNullException.ThrowIfNull( src );
		if ( offset < 0 || length < 0 || offset > src.Length - length || length % Size != 0 )
			throw new InvalidDataException( $"Invalid {typeof( TLump ).Name} lump range." );

		return Read( src.AsSpan( offset, length ), length / Size );
	}

	public static TLump ReadSingleFromStream( Stream stream )
	{
		ValidateRange( stream, stream.CanSeek ? stream.Position : 0, 1 );
		Span<byte> bytes = Size <= 1024 ? stackalloc byte[Size] : new byte[Size];
		stream.ReadExactly( bytes );

		using var reader = ByteStream.CreateReader( bytes );
		return reader.Read<TLump>();
	}

	public static void ReadLumpFromStream( Stream stream, int count, Action<TLump> handler, bool reseekPerItem = true )
	{
		ReadLumpFromStream( stream, count, ( index, lump ) => handler( lump ), reseekPerItem );
	}

	public static void ReadLumpFromStream( Stream stream, int count, Action<int, TLump> handler, bool reseekPerItem = true )
	{
		ArgumentNullException.ThrowIfNull( handler );
		var start = stream.Position;
		var items = ReadLumpFromStream( stream, count );
		var end = stream.Position;

		// Each invocation owns its records: callbacks may recursively read the same type.
		for ( var i = 0; i < count; ++i )
		{
			if ( reseekPerItem ) stream.Seek( start + (long)i * Size, SeekOrigin.Begin );
			handler( i, items[i] );
		}

		if ( reseekPerItem ) stream.Seek( end, SeekOrigin.Begin );
	}

	public static TLump[] ReadLumpFromStream( Stream stream, int count )
	{
		ValidateRange( stream, stream.CanSeek ? stream.Position : 0, count );
		if ( count == 0 )
			return Array.Empty<TLump>();

		var bytes = new byte[count * Size];
		stream.ReadExactly( bytes );
		return Read( bytes, count );
	}

}
