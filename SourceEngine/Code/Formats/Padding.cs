using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Source1;

// Fixed-size gaps in packed file records. Lump records must be unmanaged, so reserved or
// unused fields that the file format declares as arrays are sized here instead.

[StructLayout( LayoutKind.Sequential, Pack = 1, Size = 3 )]
internal struct Padding3 { }

[StructLayout( LayoutKind.Sequential, Pack = 1, Size = 20 )]
internal struct Padding20 { }

[StructLayout( LayoutKind.Sequential, Pack = 1, Size = 24 )]
internal struct Padding24 { }

[StructLayout( LayoutKind.Sequential, Pack = 1, Size = 28 )]
internal struct Padding28 { }

[StructLayout( LayoutKind.Sequential, Pack = 1, Size = 32 )]
internal struct Padding32 { }

[StructLayout( LayoutKind.Sequential, Pack = 1, Size = 40 )]
internal struct Padding40 { }

[StructLayout( LayoutKind.Sequential, Pack = 1, Size = 64 )]
internal struct Padding64 { }

/// <summary>
/// A 64-byte, null-terminated name stored inline in a record.
/// </summary>
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal struct Name64
{
	private ulong _0, _1, _2, _3, _4, _5, _6, _7;

	public override readonly string ToString()
	{
		Span<byte> bytes = stackalloc byte[64];
		BinaryPrimitives.WriteUInt64LittleEndian( bytes, _0 );
		BinaryPrimitives.WriteUInt64LittleEndian( bytes[8..], _1 );
		BinaryPrimitives.WriteUInt64LittleEndian( bytes[16..], _2 );
		BinaryPrimitives.WriteUInt64LittleEndian( bytes[24..], _3 );
		BinaryPrimitives.WriteUInt64LittleEndian( bytes[32..], _4 );
		BinaryPrimitives.WriteUInt64LittleEndian( bytes[40..], _5 );
		BinaryPrimitives.WriteUInt64LittleEndian( bytes[48..], _6 );
		BinaryPrimitives.WriteUInt64LittleEndian( bytes[56..], _7 );
		var end = bytes.IndexOf( (byte)0 );
		return Encoding.UTF8.GetString( end >= 0 ? bytes[..end] : bytes );
	}
}
