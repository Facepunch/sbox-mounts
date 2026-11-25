using Diesel;
using System.Globalization;
using System.Text;

/// <summary>
/// Research dump for mission scripts (tracker P3). For one level: the top-level <c>mission</c> resource and each
/// continent's <c>mission</c> resource / <c>mission</c> + <c>mission_scripts</c> tables, shallowly. Across all nine
/// levels: a histogram of element classes (any table with a <c>class</c> string), the value keys each class uses,
/// and two full examples per class, so a reader/executor can be designed from real data.
/// Run with: Diesel.Verify &lt;install&gt; --dump-mission [level] [output.txt]   (default level: bank)
/// </summary>
static class MissionDump
{
	static readonly string[] Levels = ["apartment", "bank", "bridge", "diamondheist", "l4d", "secret_stash", "slaughterhouse", "street", "suburbia"];

	public static void Run( BundleDatabase db, string level, TextWriter o )
	{
		var names = new Dictionary<ulong, string>();
		foreach ( var name in Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' ) )
			if ( name.Length > 0 ) names.TryAdd( IdString.Hash( name ), name );

		o.WriteLine( $"== {level}: resources named levels/{level}/... of type mission ==" );
		ulong missionType = IdString.Hash( "mission" );
		foreach ( var (key, _) in db.Entries.Where( e => e.Key.Type == missionType ) )
			if ( names.TryGetValue( key.Path, out var path ) && path.StartsWith( $"levels/{level}/", StringComparison.Ordinal ) )
			{
				var bytes = db.Find( missionType, key.Path );
				o.WriteLine( $"-- {path}.mission ({bytes.Length} bytes, starts {Convert.ToHexString( bytes.AsSpan( 0, Math.Min( 16, bytes.Length ) ) )})" );
				var table = TryScript( bytes, o );
				if ( table is not null ) Shallow( o, table, "   ", 0, 3, 12 );
			}

		var world = ScriptData.Read( db.Find( "world", $"levels/{level}/world" ) );
		o.WriteLine();
		o.WriteLine( $"== world top-level keys: {string.Join( ", ", world.Keys )} ==" );
		foreach ( var key in new[] { "mission", "mission_scripts" } )
			if ( world.Table( key ) is { } t ) { o.WriteLine( $"-- world.{key}:" ); Shallow( o, t, "   ", 0, 3, 12 ); }
		foreach ( var (continentName, continent) in Continents( db, level ) )
			foreach ( var key in new[] { "mission", "mission_scripts" } )
				if ( continent.Table( key ) is { } t ) { o.WriteLine( $"-- continent {continentName}.{key}: ({t.Count} entries)" ); Shallow( o, t, "   ", 0, 3, 8 ); }

		// Element histogram over every mission resource and continent mission table of every level.
		var classes = new SortedDictionary<string, (int Count, HashSet<string> Levels, Dictionary<string, int> Keys, List<(string Where, ScriptTable Table)> Examples)>( StringComparer.Ordinal );
		foreach ( var l in Levels )
		{
			var sources = new List<(string Where, ScriptTable Table)>();
			foreach ( var (key, _) in db.Entries.Where( e => e.Key.Type == missionType ) )
				if ( names.TryGetValue( key.Path, out var path ) && path.StartsWith( $"levels/{l}/", StringComparison.Ordinal ) && TryScript( db.Find( missionType, key.Path ), null ) is { } t )
					sources.Add( (path + ".mission", t) );
			foreach ( var (continentName, continent) in Continents( db, l ) )
				foreach ( var key in new[] { "mission", "mission_scripts" } )
					if ( continent.Table( key ) is { } t ) sources.Add( ($"levels/{l}/{continentName}.{key}", t) );
			foreach ( var (where, table) in sources )
				Walk( table, where, l, classes, [] );
		}
		o.WriteLine();
		o.WriteLine( "== element classes across all levels (count, levels, value keys) ==" );
		foreach ( var (cls, info) in classes.OrderByDescending( c => c.Value.Count ) )
			o.WriteLine( $"{info.Count,6}  {cls,-40} [{string.Join( ",", info.Levels.OrderBy( x => x ) )}]  keys: {string.Join( ", ", info.Keys.OrderByDescending( k => k.Value ).Select( k => $"{k.Key}({k.Value})" ) )}" );
		o.WriteLine();
		o.WriteLine( "== two examples per class ==" );
		foreach ( var (cls, info) in classes.OrderByDescending( c => c.Value.Count ) )
			foreach ( var (where, table) in info.Examples )
			{
				o.WriteLine( $"--- {cls} @ {where}" );
				Shallow( o, table, "    ", 0, 4, 30 );
			}
	}

	static IEnumerable<(string Name, ScriptTable Continent)> Continents( BundleDatabase db, string level )
	{
		var world = ScriptData.Read( db.Find( "world", $"levels/{level}/world" ) );
		string dir = $"levels/{level}/";
		if ( world.Table( "world_data" )?.Text( "continents_file" ) is not { } file || db.Find( "continents", dir + file ) is not { } bytes ) yield break;
		foreach ( var (name, value) in ScriptData.Read( bytes ) )
			if ( value is ScriptTable c && !c.Flag( "editor_only" ) && db.Find( "continent", $"{dir}{name}/{name}" ) is { } layer )
				yield return (name, ScriptData.Read( layer ));
	}

	static ScriptTable TryScript( byte[] bytes, TextWriter o )
	{
		try { return ScriptData.Read( bytes ); }
		catch ( Exception e )
		{
			o?.WriteLine( $"   (not script data: {e.Message}; text preview: {Preview( bytes )})" );
			return null;
		}
	}

	static string Preview( byte[] bytes ) => new string( Encoding.Latin1.GetString( bytes, 0, Math.Min( 300, bytes.Length ) ).Select( c => c is >= ' ' and <= '~' ? c : '.' ).ToArray() );

	static void Walk( ScriptTable table, string where, string level,
		SortedDictionary<string, (int Count, HashSet<string> Levels, Dictionary<string, int> Keys, List<(string Where, ScriptTable Table)> Examples)> classes, HashSet<ScriptTable> seen )
	{
		if ( !seen.Add( table ) ) return;
		if ( table.Text( "class" ) is { Length: > 0 } cls )
		{
			if ( !classes.TryGetValue( cls, out var info ) ) info = (0, new HashSet<string>(), new Dictionary<string, int>(), new List<(string, ScriptTable)>());
			info.Count++;
			info.Levels.Add( level );
			var values = table.Table( "values" ) ?? table;
			foreach ( var key in values.Keys ) info.Keys[key] = info.Keys.GetValueOrDefault( key ) + 1;
			if ( info.Examples.Count < 2 ) info.Examples.Add( (where, table) );
			classes[cls] = info;
		}
		foreach ( var child in table.Children ) Walk( child, where, level, classes, seen );
	}

	static void Shallow( TextWriter o, ScriptTable table, string indent, int depth, int maxDepth, int maxChildren )
	{
		int shown = 0;
		foreach ( var (key, value) in table )
		{
			if ( ++shown > maxChildren ) { o.WriteLine( $"{indent}... {table.Count - maxChildren} more" ); break; }
			if ( value is ScriptTable child )
			{
				o.WriteLine( $"{indent}{key}{(child.Meta is null ? "" : $" <{child.Meta}>")}{(child.Text( "class" ) is { } c ? $" class={c}" : "")}: ({child.Count})" );
				if ( depth < maxDepth ) Shallow( o, child, indent + "  ", depth + 1, maxDepth, maxChildren );
			}
			else o.WriteLine( $"{indent}{key} = {Format( value )}" );
		}
	}

	static string Format( object value ) => value switch
	{
		null => "nil",
		float f => f.ToString( "0.####", CultureInfo.InvariantCulture ),
		ulong u => $"#{u:x16}",
		_ => Convert.ToString( value, CultureInfo.InvariantCulture ),
	};
}
