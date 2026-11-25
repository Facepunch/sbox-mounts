using System.Text;

namespace Diesel;

/// <summary>
/// A Diesel <c>.strings</c> localization table (<c>strings/system_text</c>, <c>strings/menu</c>, …), a serialized vector:
/// <code>
/// header : u32 ?, u32 count, u32 capacity, u32 tableOffset (0x28), u32 allocator, …
/// entry  : u64 id IdString, u32 allocator, u32 string offset, u32 flag, u32 pad   (24 bytes)
/// strings: null-terminated UTF-8, after the table
/// </code>
/// Unused slots carry the empty id's hash and point at an empty string. Waypoint text ids (<c>wp_bank_get_drill</c>…)
/// live in <c>strings/system_text</c>.
/// </summary>
static class StringTable
{
	public static readonly string[] Files = ["system_text", "menu", "systemmenu", "dialog", "atmospheric_text", "savefile", "debug"];

	public static Dictionary<ulong, string> Read( byte[] bytes )
	{
		var data = new BinaryData( bytes );
		int count = data.Int( 4 ), capacity = data.Int( 8 ), table = data.Int( 12 );
		if ( count < 0 || capacity < count || count > 1_000_000 ) throw new InvalidDataException( "Invalid strings entry count." );
		data.Check( table, (long)count * 24 );
		var result = new Dictionary<ulong, string>( count );
		for ( int i = 0; i < count; i++ )
		{
			int p = table + i * 24;
			ulong id = data.ULong( p );
			int offset = data.Int( p + 12 );
			if ( offset <= 0 || offset >= bytes.Length ) continue;
			int end = Array.IndexOf( bytes, (byte)0, offset );
			if ( end < 0 ) continue;
			string text = Encoding.UTF8.GetString( bytes, offset, end - offset );
			if ( text.Length > 0 ) result.TryAdd( id, text );
		}
		return result;
	}

	/// <summary>All shipped tables merged (earlier files win on duplicate ids).</summary>
	public static Dictionary<ulong, string> ReadAll( BundleDatabase database )
	{
		var merged = new Dictionary<ulong, string>();
		foreach ( var file in Files )
		{
			if ( database.Find( "strings", $"strings/{file}" ) is not { } bytes ) continue;
			try
			{
				foreach ( var (id, text) in Read( bytes ) ) merged.TryAdd( id, text );
			}
			catch ( InvalidDataException ) { }
		}
		return merged;
	}

	/// <summary>Readable fallback for an unresolved id: <c>bank_get_drill</c> → <c>Bank get drill</c>.</summary>
	public static string Humanize( string id )
	{
		if ( string.IsNullOrEmpty( id ) ) return "";
		var words = id.Replace( '_', ' ' ).Trim();
		return words.Length == 0 ? id : char.ToUpperInvariant( words[0] ) + words[1..];
	}
}
