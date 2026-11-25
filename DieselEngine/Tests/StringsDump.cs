using Diesel;
using System.Text;

/// <summary>
/// Research dump for objective/waypoint text (tracker P3): every resource whose name looks like localization, its types
/// and first bytes; IdString hashes of the missions' objective/waypoint text ids found inside those resources (reveals the
/// table layout, like the massunit probe); and the objective/waypoint/operator values the missions use.
/// Run with: Diesel.Verify &lt;install&gt; --dump-strings [output.txt]
/// </summary>
static class StringsDump
{
	static readonly string[] Levels = ["apartment", "bank", "bridge", "diamondheist", "l4d", "secret_stash", "slaughterhouse", "street", "suburbia"];

	public static void Run( BundleDatabase db, TextWriter o )
	{
		var names = new Dictionary<ulong, string>();
		foreach ( var name in Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' ) )
			if ( name.Length > 0 ) names.TryAdd( IdString.Hash( name ), name );
		foreach ( var type in new[] { "strings", "string", "localization", "loc", "font", "xml", "lua", "texture" } ) names.TryAdd( IdString.Hash( type ), type );
		string Name( ulong hash ) => names.TryGetValue( hash, out var n ) ? n : $"#{hash:x16}";

		// Mission text ids and values.
		var textIds = new SortedSet<string>( StringComparer.Ordinal );
		var objectiveStates = new Dictionary<string, int>();
		var icons = new Dictionary<string, int>();
		var operations = new Dictionary<string, int>();
		o.WriteLine( "== objective / waypoint samples ==" );
		int shown = 0;
		foreach ( var level in Levels )
		{
			var mission = MissionData.Read( db, $"levels/{level}" );
			foreach ( var element in mission.Elements.Values )
			{
				switch ( element.Class )
				{
					case "ElementObjective":
						if ( element.Text( "objective" ) is { Length: > 0 } objective ) textIds.Add( objective );
						if ( element.Text( "sub_objective" ) is { Length: > 0 } sub && sub != "none" ) textIds.Add( sub );
						string state = element.Text( "state" ) ?? "nil";
						objectiveStates[state] = objectiveStates.GetValueOrDefault( state ) + 1;
						if ( shown++ < 25 ) o.WriteLine( $"  [{level}] objective {element.Id}: objective={element.Text( "objective" )} sub={element.Text( "sub_objective" )} state={state}" );
						break;
					case "ElementWaypoint":
						if ( element.Text( "text_id" ) is { Length: > 0 } text ) textIds.Add( text );
						string icon = element.Text( "icon" ) ?? "nil";
						icons[icon] = icons.GetValueOrDefault( icon ) + 1;
						break;
					case "ElementOperator":
						string operation = element.Text( "operation" ) ?? "nil";
						var targets = element.Ids( "elements" ).Select( id => mission.Elements.GetValueOrDefault( id )?.Class ?? "?" );
						foreach ( var target in targets.Distinct() ) operations[$"{operation} -> {target}"] = operations.GetValueOrDefault( $"{operation} -> {target}" ) + 1;
						break;
				}
			}
		}
		o.WriteLine( $"  objective states: {string.Join( ", ", objectiveStates.OrderByDescending( s => s.Value ).Select( s => $"{s.Key}×{s.Value}" ) )}" );
		o.WriteLine( $"  waypoint icons: {string.Join( ", ", icons.OrderByDescending( s => s.Value ).Select( s => $"{s.Key}×{s.Value}" ) )}" );
		o.WriteLine( $"  operator operation -> target class: {string.Join( ", ", operations.OrderByDescending( s => s.Value ).Select( s => $"{s.Key}×{s.Value}" ) )}" );
		o.WriteLine( $"  {textIds.Count} distinct text ids, e.g. {string.Join( ", ", textIds.Take( 30 ) )}" );

		// Candidate localization resources.
		o.WriteLine();
		o.WriteLine( "== resources whose name mentions strings/loc/lang/english ==" );
		var candidates = db.Entries.Values
			.Where( e => names.TryGetValue( e.Path, out var n ) && (n.Contains( "string", StringComparison.OrdinalIgnoreCase ) || n.Contains( "loc", StringComparison.OrdinalIgnoreCase )
				|| n.Contains( "english", StringComparison.OrdinalIgnoreCase ) || n.Contains( "lang", StringComparison.OrdinalIgnoreCase )) && !n.StartsWith( "units/", StringComparison.Ordinal )
				&& !n.StartsWith( "levels/", StringComparison.Ordinal ) && !n.Contains( "/textures/" ) )
			.OrderBy( e => Name( e.Path ) ).ToList();
		foreach ( var entry in candidates.Take( 80 ) )
			o.WriteLine( $"  {Name( entry.Path )} [{Name( entry.Type )}] {entry.Length} bytes" );
		o.WriteLine( "  types present in the database: " + string.Join( ", ", db.Entries.Keys.Select( k => k.Type ).Distinct().Select( Name ).OrderBy( n => n ) ) );

		// Objective definitions: mission objective ids aren't string ids, so they must map through these.
		o.WriteLine();
		o.WriteLine( "== resources of type objective ==" );
		ulong objectiveType = IdString.Hash( "objective" );
		foreach ( var entry in db.Entries.Values.Where( e => e.Type == objectiveType ).Take( 6 ) )
		{
			var bytes = db.Read( entry );
			string text = Encoding.UTF8.GetString( bytes );
			o.WriteLine( $"-- {Name( entry.Path )} ({bytes.Length} bytes)" );
			o.WriteLine( text.Length > 4000 ? text[..4000] : text );
		}
		var localized = StringTable.ReadAll( db );
		o.WriteLine( $"strings tables: {localized.Count} entries; sample lookups:" );
		foreach ( var sample in new[] { "wp_bank_get_drill", "wp_ap_roof", "bank_get_drill", "bank_get_drill_hl", "ap_intro", "ap_intro_hl", "ap_intro_desc" } )
			o.WriteLine( $"  {sample} -> {localized.GetValueOrDefault( IdString.Hash( sample ) ) ?? "(none)"}" );

		// Probe: where do the text ids' hashes occur in candidate resources?
		var wanted = new Dictionary<ulong, string>();
		foreach ( var id in textIds ) wanted.TryAdd( IdString.Hash( id ), id );
		o.WriteLine();
		o.WriteLine( "== hash probe: text id IdStrings inside candidate resources ==" );
		foreach ( var entry in candidates.Where( e => Name( e.Type ) is not ("texture" or "lua") ) )
		{
			var bytes = db.Read( entry );
			var hits = new List<(int Offset, string Id)>();
			for ( int p = 0; p + 8 <= bytes.Length; p++ )
				if ( wanted.TryGetValue( BitConverter.ToUInt64( bytes, p ), out var id ) ) hits.Add( (p, id) );
			if ( hits.Count == 0 ) continue;
			o.WriteLine( $"-- {Name( entry.Path )} [{Name( entry.Type )}] {bytes.Length} bytes: {hits.Count} hits" );
			o.WriteLine( "   first 96 bytes: " + Convert.ToHexString( bytes.AsSpan( 0, Math.Min( 96, bytes.Length ) ) ) );
			foreach ( var (offset, id) in hits.Take( 12 ) )
			{
				var around = bytes.AsSpan( Math.Max( 0, offset - 8 ), Math.Min( 32, bytes.Length - Math.Max( 0, offset - 8 ) ) );
				o.WriteLine( $"   @{offset:x6} {id}: {Convert.ToHexString( around )}" );
			}
			var gaps = hits.Zip( hits.Skip( 1 ), ( a, b ) => b.Offset - a.Offset ).GroupBy( g => g ).OrderByDescending( g => g.Count() ).Take( 5 );
			o.WriteLine( "   common gaps: " + string.Join( ", ", gaps.Select( g => $"{g.Key}×{g.Count()}" ) ) );
			// Plain-text neighbourhood of the first few hits, in case strings sit nearby.
			o.WriteLine( "   text near start: " + new string( Encoding.UTF8.GetString( bytes, 0, Math.Min( bytes.Length, 400 ) ).Select( c => c is >= ' ' and <= '~' ? c : '.' ).ToArray() ) );
		}
	}
}
