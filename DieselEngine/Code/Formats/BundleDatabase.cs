namespace Diesel;

/// <summary>Windows PAYDAY: The Heist BLB and split bundle headers. Resources stay in the installed game.</summary>
sealed class BundleDatabase
{
	public record Entry( ulong Type, ulong Path, string Bundle, long Offset, int Length );
	public Dictionary<(ulong Type, ulong Path), Entry> Entries { get; } = [];
	readonly Func<string, Stream> open;

	public BundleDatabase( Func<string, Stream> open, IEnumerable<string> headers )
	{
		this.open = open;
		var db = new BinaryData( ReadFile( "assets/all.blb" ) );
		int count = db.Int( 28 ), start = db.Int( 36 );
		db.Check( start, (long)count * 32 );
		var ids = new Dictionary<uint, (ulong Type, ulong Path)>();
		for ( int i = 0; i < count; i++ )
		{
			int p = start + i * 32;
			if ( db.UInt( p + 16 ) == 0 )
				ids[db.UInt( p + 24 )] = (db.ULong( p ), db.ULong( p + 8 ));
		}

		// all_N bundles have explicit sizes; prefer these canonical copies over package duplicates.
		foreach ( var path in headers.OrderBy( x => Path.GetFileName( x ).StartsWith( "all_" ) ? 0 : 1 ).ThenBy( x => x, StringComparer.Ordinal ) )
		{
			var h = new BinaryData( ReadFile( path ) );
			bool sized = h.UInt( 4 ) != h.UInt( 8 );
			int n = h.Int( sized ? 8 : 4 );
			int offset = h.Int( sized ? 16 : 12 );
			int stride = sized ? 12 : 8;
			h.Check( (long)offset + 4, (long)n * stride );
			string bundle = path.Replace( "_h.bundle", ".bundle" );
			using var stream = open( bundle );
			for ( int i = 0; i < n; i++ )
			{
				int p = offset + 4 + i * stride;
				long address = h.UInt( p + 4 );
				long size = sized ? h.UInt( p + 8 ) : (i + 1 < n ? h.UInt( p + stride + 4 ) : stream.Length) - address;
				if ( address > stream.Length || size < 0 || size > stream.Length - address || size > int.MaxValue )
					throw new InvalidDataException( $"Invalid resource extent in {path}." );
				if ( size > 0 && ids.TryGetValue( h.UInt( p ), out var key ) )
					Entries.TryAdd( key, new Entry( key.Type, key.Path, bundle, address, (int)size ) );
			}
		}
	}

	byte[] ReadFile( string path )
	{
		using var stream = open( path );
		var bytes = new byte[checked((int)stream.Length)];
		stream.ReadExactly( bytes );
		return bytes;
	}

	public byte[] Read( Entry entry )
	{
		using var stream = open( entry.Bundle );
		stream.Position = entry.Offset;
		var bytes = new byte[entry.Length];
		stream.ReadExactly( bytes );
		return bytes;
	}

	public byte[] Find( string type, string path ) => Find( IdString.Hash( type ), IdString.Hash( path ) );
	public byte[] Find( ulong type, ulong path ) => Entries.TryGetValue( (type, path), out var entry ) ? Read( entry ) : null;
}
