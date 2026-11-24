using System.Text;

namespace Diesel;

/// <summary>Diesel's 64-bit Jenkins lookup8 identifier (seed zero).</summary>
static class IdString
{
	public static ulong Hash( string text )
	{
		var bytes = Encoding.UTF8.GetBytes( text );
		ulong a = 0, b = 0, c = 0x9e3779b97f4a7c13;
		unchecked
		{
			int offset = 0;
			for ( ; offset + 24 <= bytes.Length; offset += 24 )
			{
				a += BitConverter.ToUInt64( bytes, offset );
				b += BitConverter.ToUInt64( bytes, offset + 8 );
				c += BitConverter.ToUInt64( bytes, offset + 16 );
				Mix( ref a, ref b, ref c );
			}
			c += (ulong)bytes.Length;
			for ( int i = 0; offset + i < bytes.Length; i++ )
			{
				ulong v = bytes[offset + i];
				if ( i < 8 ) a += v << (i * 8);
				else if ( i < 16 ) b += v << ((i - 8) * 8);
				else c += v << ((i - 15) * 8);
			}
			Mix( ref a, ref b, ref c );
			return c;
		}
	}

	static void Mix( ref ulong a, ref ulong b, ref ulong c )
	{
		unchecked
		{
			a = (a - b - c) ^ (c >> 43); b = (b - c - a) ^ (a << 9); c = (c - a - b) ^ (b >> 8);
			a = (a - b - c) ^ (c >> 38); b = (b - c - a) ^ (a << 23); c = (c - a - b) ^ (b >> 5);
			a = (a - b - c) ^ (c >> 35); b = (b - c - a) ^ (a << 49); c = (c - a - b) ^ (b >> 11);
			a = (a - b - c) ^ (c >> 12); b = (b - c - a) ^ (a << 18); c = (c - a - b) ^ (b >> 22);
		}
	}
}
