using Diesel;
using System.Globalization;
using System.Text;

/// <summary>
/// Research dump for brush/massunit placements (tracker P2). Prints the world's <c>brush</c> table and
/// probes the binary <c>levels/&lt;map&gt;/massunit</c> resource: header words, IdString matches against the
/// game's lookup (unit names), the spacing between them, and the floats that follow each match.
/// Run with: Diesel.Verify &lt;install&gt; --dump-brush [level] [output.txt]   (default level: bank)
/// </summary>
static class BrushDump
{
	public static void Run( BundleDatabase db, string level, TextWriter o )
	{
		var names = new Dictionary<ulong, string>();
		foreach ( var name in Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' ) )
			if ( name.Length > 0 ) names.TryAdd( IdString.Hash( name ), name );

		string worldPath = $"levels/{level}/world";
		var world = ScriptData.Read( db.Find( "world", worldPath ) ?? throw new FileNotFoundException( worldPath ) );
		o.WriteLine( $"== {worldPath} brush table ==" );
		Dump( o, world.Table( "brush" ), "  ", 0, 5, 80 );

		// Continents can carry their own brush data too.
		string dir = $"levels/{level}/";
		if ( world.Table( "world_data" )?.Text( "continents_file" ) is { } continentsFile && db.Find( "continents", dir + continentsFile ) is { } cbytes )
			foreach ( var (name, _) in ScriptData.Read( cbytes ) )
				if ( db.Find( "continent", $"{dir}{name}/{name}" ) is { } layer && ScriptData.Read( layer ) is { } continent )
				{
					o.WriteLine( $"-- continent {name}: keys = {string.Join( ", ", continent.Keys )}" );
					if ( continent.Table( "brush" ) is { } brush ) Dump( o, brush, "    ", 0, 4, 40 );
				}

		var bytes = db.Find( "massunit", $"levels/{level}/massunit" );
		o.WriteLine();
		o.WriteLine( $"== levels/{level}/massunit: {(bytes is null ? "missing" : $"{bytes.Length} bytes")} ==" );
		if ( bytes is null ) return;
		o.WriteLine( "first 256 bytes:" );
		for ( int row = 0; row < Math.Min( 256, bytes.Length ); row += 16 )
		{
			var span = bytes.AsSpan( row, Math.Min( 16, bytes.Length - row ) );
			o.Write( $"  {row:x6}: {Convert.ToHexString( span ),-32}  " );
			for ( int i = 0; i + 4 <= span.Length; i += 4 )
				o.Write( $"u{BitConverter.ToUInt32( span[i..] ),-11} f{Float( BitConverter.ToSingle( span[i..] ) ),-12} " );
			o.WriteLine();
		}

		var matches = new List<(int Offset, string Name)>();
		for ( int p = 0; p + 8 <= bytes.Length; p++ )
			if ( names.TryGetValue( BitConverter.ToUInt64( bytes, p ), out var match ) )
				matches.Add( (p, match) );
		o.WriteLine();
		o.WriteLine( $"IdString matches: {matches.Count} ({matches.Select( m => m.Name ).Distinct().Count()} distinct)" );
		for ( int i = 0; i < Math.Min( 200, matches.Count ); i++ )
		{
			var (offset, name) = matches[i];
			int gap = i == 0 ? offset : offset - matches[i - 1].Offset;
			var after = new StringBuilder();
			for ( int j = 8; j < 8 + 48 && offset + j + 4 <= bytes.Length; j += 4 )
				after.Append( $"{BitConverter.ToUInt32( bytes, offset + j )}/{Float( BitConverter.ToSingle( bytes, offset + j ) )} " );
			o.WriteLine( $"  @{offset:x6} (+{gap}) {name}" );
			o.WriteLine( $"      next words (u32/f32): {after}" );
		}
		var gaps = matches.Zip( matches.Skip( 1 ), ( a, b ) => b.Offset - a.Offset ).GroupBy( g => g ).OrderByDescending( g => g.Count() ).Take( 10 );
		o.WriteLine( "most common gaps between matches: " + string.Join( ", ", gaps.Select( g => $"{g.Key}×{g.Count()}" ) ) );

		// Are the matched names units? Report which resource types exist for them.
		ulong unitType = IdString.Hash( "unit" );
		foreach ( var name in matches.Select( m => m.Name ).Distinct().Take( 40 ) )
			o.WriteLine( $"  {name}: unit={db.Entries.ContainsKey( (unitType, IdString.Hash( name )) )}" );

		// Summary across all levels so format assumptions can be checked against every file.
		o.WriteLine();
		o.WriteLine( "== all levels ==" );
		foreach ( var other in new[] { "apartment", "bank", "bridge", "diamondheist", "l4d", "secret_stash", "slaughterhouse", "street", "suburbia" } )
		{
			var b = db.Find( "massunit", $"levels/{other}/massunit" );
			var w = ScriptData.Read( db.Find( "world", $"levels/{other}/world" ) );
			int brushEntries = w.Table( "brush" )?.Count ?? -1;
			o.WriteLine( $"  {other}: massunit {(b is null ? "missing" : $"{b.Length} bytes, first words {string.Join( " ", Enumerable.Range( 0, Math.Min( 6, b.Length / 4 ) ).Select( i => BitConverter.ToUInt32( b, i * 4 ) ) )}")}; world.brush entries {brushEntries}" );
		}
	}

	static string Float( float f ) => float.IsFinite( f ) && MathF.Abs( f ) < 1e7f && (f == 0 || MathF.Abs( f ) > 1e-5f) ? f.ToString( "0.###", CultureInfo.InvariantCulture ) : "-";

	static void Dump( TextWriter o, ScriptTable table, string indent, int depth, int maxDepth, int maxChildren )
	{
		if ( table is null ) { o.WriteLine( indent + "(missing)" ); return; }
		int shown = 0;
		foreach ( var (key, value) in table )
		{
			if ( ++shown > maxChildren ) { o.WriteLine( $"{indent}... {table.Count - maxChildren} more" ); break; }
			if ( value is ScriptTable child )
			{
				o.WriteLine( $"{indent}{key}{(child.Meta is null ? "" : $" <{child.Meta}>")}: ({child.Count} entries)" );
				if ( depth < maxDepth ) Dump( o, child, indent + "  ", depth + 1, maxDepth, maxChildren );
			}
			else o.WriteLine( $"{indent}{key} = {Convert.ToString( value, CultureInfo.InvariantCulture )}" );
		}
	}
}
