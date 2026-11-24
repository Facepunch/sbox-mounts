using System.Text;

namespace Diesel;

/// <summary>Bounds-checked, little-endian reads of offsets relative to a resource.</summary>
sealed class BinaryData( byte[] bytes )
{
	public int Length => bytes.Length;
	public void Check( long offset, long length )
	{
		if ( offset < 0 || length < 0 || offset > bytes.Length - length )
			throw new InvalidDataException( $"Diesel resource range {offset}+{length} exceeds {bytes.Length} bytes." );
	}
	public int Int( int offset ) { Check( offset, 4 ); return BitConverter.ToInt32( bytes, offset ); }
	public uint UInt( int offset ) => unchecked((uint)Int( offset ));
	public ulong ULong( int offset ) { Check( offset, 8 ); return BitConverter.ToUInt64( bytes, offset ); }
	public ushort UShort( int offset ) { Check( offset, 2 ); return BitConverter.ToUInt16( bytes, offset ); }
	public float Float( int offset ) { Check( offset, 4 ); return BitConverter.ToSingle( bytes, offset ); }
	public string String( int offset )
	{
		Check( offset, 1 );
		int end = Array.IndexOf( bytes, (byte)0, offset );
		if ( end < 0 ) throw new InvalidDataException( "Unterminated Diesel string." );
		return Encoding.Latin1.GetString( bytes, offset, end - offset );
	}
}
