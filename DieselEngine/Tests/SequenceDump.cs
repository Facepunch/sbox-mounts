using Diesel;
using System.Globalization;
using System.Text;

/// <summary>
/// Research dump for unit damage rules: prints the full sequence_manager script table of given units (short names are
/// resolved against the installed lookup), so per-body endurance (damage type, threshold, sequence to run) can be
/// read for generic breakables. Also counts, across every unit, which top-level keys and body/endurance shapes exist.
/// Run with: Diesel.Verify &lt;install&gt; --dump-sequence [unit short names, comma-separated] [output.txt]
/// </summary>
static class SequenceDump
{
	public static void Run( BundleDatabase db, string units, TextWriter o )
	{
		var names = Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' ).Where( n => n.Length > 0 ).Distinct().ToList();
		ulong unitType = IdString.Hash( "unit" );
		var unitPaths = names.Where( n => db.Entries.ContainsKey( (unitType, IdString.Hash( n )) ) ).ToList();

		foreach ( var wanted in units.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			var path = unitPaths.FirstOrDefault( p => p == wanted ) ?? unitPaths.FirstOrDefault( p => p.EndsWith( "/" + wanted, StringComparison.Ordinal ) );
			o.WriteLine( $"==================== {wanted} -> {path ?? "NOT FOUND"} ====================" );
			if ( path is null ) continue;
			var unitXml = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "unit", path ) ) );
			string objectPath = unitXml.Element( "object" )?.Attribute( "file" );
			var objectBytes = objectPath is null ? null : db.Find( "object", objectPath );
			if ( objectBytes is null ) { o.WriteLine( "  (no object)" ); continue; }
			var objXml = XmlNode.Parse( Encoding.UTF8.GetString( objectBytes ) );
			o.WriteLine( $"  graphics: {string.Join( ", ", UnitGeometry.GraphicsObjects( objXml ).Select( g => $"{g.Attribute( "name" )}{(g.Attribute( "enabled" ) == "true" ? "" : "(off)")}" ) )}" );
			o.WriteLine( $"  bodies: {string.Join( ", ", (objXml.Element( "bodies" )?.Elements( "body" ) ?? []).Select( b => $"{b.Attribute( "name" )}{(b.Attribute( "enabled" ) == "false" ? "(off)" : "")}" ) )}" );
			if ( objXml.Element( "sequence_manager" )?.Attribute( "file" ) is not string smPath || db.Find( "sequence_manager", smPath ) is not { } smBytes )
			{
				o.WriteLine( "  (no sequence_manager)" );
				continue;
			}
			o.WriteLine( $"  sequence_manager {smPath}:" );
			Tree( o, ScriptData.Read( smBytes ), "    ", [] );
		}

		// Shape survey over every unit's sequence manager: which node kinds (meta) occur under unit/body, and how often.
		o.WriteLine();
		o.WriteLine( "==================== survey: node kinds under sequence_manager/unit ====================" );
		var kinds = new Dictionary<string, int>();
		var bodyChildKinds = new Dictionary<string, int>();
		int managers = 0, withBodies = 0;
		var seen = new HashSet<string>();
		foreach ( var path in unitPaths )
		{
			try
			{
				var unitXml = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "unit", path ) ) );
				string objectPath = unitXml.Element( "object" )?.Attribute( "file" );
				if ( objectPath is null || db.Find( "object", objectPath ) is not { } objectBytes ) continue;
				var objXml = XmlNode.Parse( Encoding.UTF8.GetString( objectBytes ) );
				if ( objXml.Element( "sequence_manager" )?.Attribute( "file" ) is not string smPath || !seen.Add( smPath ) ) continue;
				if ( db.Find( "sequence_manager", smPath ) is not { } smBytes ) continue;
				var unit = ScriptData.Read( smBytes ).Table( "unit" );
				if ( unit is null ) continue;
				managers++;
				bool hasBody = false;
				foreach ( var child in unit.Children )
				{
					string kind = child.Meta ?? "(table)";
					kinds[kind] = kinds.GetValueOrDefault( kind ) + 1;
					if ( kind != "body" ) continue;
					hasBody = true;
					foreach ( var bodyChild in child.Children )
					{
						string k = bodyChild.Meta ?? "(table)";
						bodyChildKinds[k] = bodyChildKinds.GetValueOrDefault( k ) + 1;
						foreach ( var inner in bodyChild.Children )
						{
							string ik = $"{k}/{inner.Meta ?? "(table)"}";
							bodyChildKinds[ik] = bodyChildKinds.GetValueOrDefault( ik ) + 1;
						}
					}
				}
				if ( hasBody ) withBodies++;
			}
			catch ( Exception ) { }
		}
		o.WriteLine( $"  {managers} distinct sequence managers, {withBodies} with body sections" );
		o.WriteLine( "  unit children: " + string.Join( ", ", kinds.OrderByDescending( k => k.Value ).Select( k => $"{k.Key}×{k.Value}" ) ) );
		o.WriteLine( "  body children: " + string.Join( ", ", bodyChildKinds.OrderByDescending( k => k.Value ).Take( 40 ).Select( k => $"{k.Key}×{k.Value}" ) ) );
	}

	internal static void Tree( TextWriter o, ScriptTable table, string indent, HashSet<ScriptTable> seen )
	{
		if ( !seen.Add( table ) || indent.Length > 40 ) return;
		foreach ( var (key, value) in table.Where( p => p.Value is not ScriptTable ) )
			o.WriteLine( $"{indent}{key} = {Format( value )}" );
		foreach ( var (key, value) in table.Where( p => p.Value is ScriptTable ) )
		{
			var child = (ScriptTable)value;
			o.WriteLine( $"{indent}[{key}] {child.Meta ?? ""}" );
			Tree( o, child, indent + "  ", seen );
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
