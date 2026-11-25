using Diesel;
using System.Globalization;
using System.Text;

/// <summary>
/// Research dump for general sound import (tracker P2): soundbanks and streams, the bnkinfo/banksinfo resources, a level's
/// world_sounds, and event-name coverage. Wwise events are keyed by FNV-1 hashes of their names, so every bank event needs a
/// name source; candidates are gathered from bnkinfo/banksinfo text, mission PlaySound elements, sequence-manager sound actions
/// and world_sounds, then matched against each bank's event ids.
/// Run with: Diesel.Verify &lt;install&gt; --dump-sound [level] [output.txt]   (default level: bank)
/// </summary>
static class SoundDump
{
	static readonly string[] Levels = ["apartment", "bank", "bridge", "diamondheist", "l4d", "secret_stash", "slaughterhouse", "street", "suburbia"];

	public static void Run( BundleDatabase db, string level, TextWriter o )
	{
		var names = new Dictionary<ulong, string>();
		foreach ( var name in Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' ) )
			if ( name.Length > 0 ) names.TryAdd( IdString.Hash( name ), name );
		string Name( ulong hash ) => names.TryGetValue( hash, out var n ) ? n : $"#{hash:x16}";
		ulong Type( string t ) => IdString.Hash( t );

		var banks = db.Entries.Values.Where( e => e.Type == Type( "bnk" ) ).OrderBy( e => Name( e.Path ) ).ToList();
		int streams = db.Entries.Keys.Count( k => k.Type == Type( "stream" ) );
		o.WriteLine( $"== {banks.Count} soundbanks (bnk), {streams} stream entries ==" );
		foreach ( var bank in banks ) o.WriteLine( $"  {Name( bank.Path )} ({bank.Length} bytes)" );

		// bnkinfo / banksinfo: format unknown; show text or script tree or hex + strings.
		foreach ( var type in new[] { "banksinfo", "bnkinfo" } )
		{
			var entries = db.Entries.Values.Where( e => e.Type == Type( type ) ).ToList();
			o.WriteLine();
			o.WriteLine( $"== {type}: {entries.Count} resources ==" );
			foreach ( var entry in entries.Take( type == "bnkinfo" ? 4 : 2 ) )
			{
				var bytes = db.Read( entry );
				o.WriteLine( $"-- {Name( entry.Path )} ({bytes.Length} bytes), first 64: {Convert.ToHexString( bytes.AsSpan( 0, Math.Min( 64, bytes.Length ) ) )}" );
				Show( o, bytes );
			}
			if ( type == "bnkinfo" ) o.WriteLine( "  names: " + string.Join( ", ", entries.Select( e => Name( e.Path ) ).OrderBy( n => n ).Take( 60 ) ) );
		}

		// world_sounds for one level.
		o.WriteLine();
		var worldSounds = db.Find( "world_sounds", $"levels/{level}/world_sounds" );
		o.WriteLine( $"== levels/{level}/world_sounds: {(worldSounds is null ? "missing" : $"{worldSounds.Length} bytes")} ==" );
		if ( worldSounds is not null ) Show( o, worldSounds );

		// Candidate event names from every referencing source.
		var candidates = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase ); // name -> source
		void Add( string name, string source ) { if ( !string.IsNullOrWhiteSpace( name ) ) candidates.TryAdd( name.Trim(), source ); }
		foreach ( var l in Levels )
		{
			foreach ( var element in MissionData.Read( db, $"levels/{l}" ).Elements.Values.Where( e => e.Class == "ElementPlaySound" ) )
				Add( element.Text( "sound_event" ), "mission" );
			if ( db.Find( "world_sounds", $"levels/{l}/world_sounds" ) is { } ws )
				foreach ( var s in Strings( ws ) ) Add( s, "world_sounds" );
		}
		int managers = 0;
		foreach ( var entry in db.Entries.Values.Where( e => e.Type == Type( "sequence_manager" ) ) )
		{
			try
			{
				managers++;
				Walk( ScriptData.Read( db.Read( entry ) ), t =>
				{
					if ( t.Meta == "sound" && SequenceVisibility.Literal( t.Text( "event" ) ) is { } ev ) Add( ev, "sequence" );
				} );
			}
			catch ( Exception ) { }
		}
		foreach ( var entry in db.Entries.Values.Where( e => e.Type == Type( "bnkinfo" ) || e.Type == Type( "banksinfo" ) ) )
			foreach ( var s in Strings( db.Read( entry ) ) ) Add( s, "bnkinfo" );
		o.WriteLine();
		o.WriteLine( $"== candidate event names: {candidates.Count} ({string.Join( ", ", candidates.GroupBy( c => c.Value ).Select( g => $"{g.Key} {g.Count()}" ) )}; {managers} sequence managers scanned) ==" );
		o.WriteLine( "  sample: " + string.Join( ", ", candidates.Keys.OrderBy( k => k ).Take( 60 ) ) );

		// Per-bank event coverage.
		o.WriteLine();
		o.WriteLine( "== per-bank events and name coverage ==" );
		var hashed = candidates.Keys.GroupBy( WwiseBank.Hash ).ToDictionary( g => g.Key, g => g.First() );
		int totalEvents = 0, totalNamed = 0;
		foreach ( var entry in banks )
		{
			string bankName = Name( entry.Path );
			try
			{
				string streamDir = $"soundbanks/streamed/{bankName[(bankName.LastIndexOf( '/' ) + 1)..]}/";
				var bank = new WwiseBank( db.Read( entry ), id => db.Find( "stream", streamDir + id ) );
				var events = bank.EventIds.ToList();
				int named = events.Count( hashed.ContainsKey );
				totalEvents += events.Count; totalNamed += named;
				o.WriteLine( $"  {bankName}: {events.Count} events, {named} named; objects {string.Join( " ", bank.ObjectTypeCounts.OrderBy( t => t.Key ).Select( t => $"t{t.Key}×{t.Value}" ) )}"
					+ (named > 0 ? $"; e.g. {string.Join( ", ", events.Where( hashed.ContainsKey ).Take( 5 ).Select( e => hashed[e] ) )}" : "") );
			}
			catch ( Exception e ) { o.WriteLine( $"  {bankName}: unreadable ({e.Message})" ); }
		}
		o.WriteLine( $"  total: {totalEvents} events, {totalNamed} named by candidates" );
	}

	static void Walk( ScriptTable table, Action<ScriptTable> visit, HashSet<ScriptTable> seen = null )
	{
		seen ??= [];
		if ( !seen.Add( table ) ) return;
		visit( table );
		foreach ( var child in table.Children ) Walk( child, visit, seen );
	}

	/// <summary>Printable runs (≥ 3 chars) that look like identifiers, from any binary or text resource.</summary>
	static IEnumerable<string> Strings( byte[] bytes ) =>
		System.Text.RegularExpressions.Regex.Matches( Encoding.Latin1.GetString( bytes ), "[A-Za-z0-9_/.-]{3,}" ).Select( m => m.Value ).Distinct();

	static void Show( TextWriter o, byte[] bytes )
	{
		bool text = bytes.Take( 200 ).All( b => b is 9 or 10 or 13 || (b >= 32 && b < 127) );
		if ( text ) { var s = Encoding.UTF8.GetString( bytes ); o.WriteLine( s.Length > 3000 ? s[..3000] : s ); return; }
		try
		{
			var table = ScriptData.Read( bytes );
			Tree( o, table, "    ", 0, [] );
			return;
		}
		catch ( Exception ) { }
		o.WriteLine( "    strings: " + string.Join( " | ", Strings( bytes ).Take( 120 ) ) );
	}

	static void Tree( TextWriter o, ScriptTable table, string indent, int depth, HashSet<ScriptTable> seen )
	{
		if ( !seen.Add( table ) || depth > 5 ) return;
		int shown = 0;
		foreach ( var (key, value) in table )
		{
			if ( ++shown > 25 ) { o.WriteLine( $"{indent}... {table.Count - 25} more" ); break; }
			if ( value is ScriptTable child ) { o.WriteLine( $"{indent}[{key}] {child.Meta}" ); Tree( o, child, indent + "  ", depth + 1, seen ); }
			else o.WriteLine( $"{indent}{key} = {(value is float f ? f.ToString( "0.###", CultureInfo.InvariantCulture ) : Convert.ToString( value, CultureInfo.InvariantCulture ))}" );
		}
	}
}
