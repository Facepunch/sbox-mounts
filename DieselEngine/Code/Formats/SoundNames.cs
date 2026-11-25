using System.Text;
using System.Text.RegularExpressions;

namespace Diesel;

/// <summary>
/// Wwise event names for PDTH's soundbanks. Banks key events by FNV-1 hashes of their names and ship no name table
/// (<c>banksinfo/existing_banks</c> holds bank names and hashes only; <c>bnkinfo/init</c> holds acoustics presets), so names
/// are harvested from everything that references sounds:
/// <list type="bullet">
/// <item>mission <c>ElementPlaySound.sound_event</c></item>
/// <item><c>levels/&lt;map&gt;/world_sounds</c> (emitter events, default ambience/occasionals/acoustics)</item>
/// <item>sequence-manager <c>sound</c> actions (<c>event = 'window_large_shatter'</c>)</item>
/// <item>identifier-like string constants in compiled Lua (sound calls keep their literals)</item>
/// </list>
/// Callers match candidates against bank event ids by hash; unmatched events stay reachable by id.
/// </summary>
static class SoundNames
{
	static readonly Regex Identifier = new( "[a-z][a-z0-9_]{2,63}" );

	public static Dictionary<uint, string> Collect( BundleDatabase database, IEnumerable<string> levelDirectories )
	{
		var names = new HashSet<string>( StringComparer.Ordinal );
		void Add( string name ) { if ( !string.IsNullOrWhiteSpace( name ) ) names.Add( name.Trim().ToLowerInvariant() ); }

		foreach ( var level in levelDirectories )
		{
			try
			{
				foreach ( var element in MissionData.Read( database, level ).Elements.Values )
					if ( element.Class == "ElementPlaySound" ) Add( element.Text( "sound_event" ) );
			}
			catch ( InvalidDataException ) { }
			if ( database.Find( "world_sounds", $"{level}/world_sounds" ) is { } worldSounds )
			{
				try { Walk( ScriptData.Read( worldSounds ), t => { foreach ( var value in t.Values.OfType<string>() ) Add( value ); } ); }
				catch ( InvalidDataException ) { }
			}
		}

		ulong sequenceType = IdString.Hash( "sequence_manager" ), luaType = IdString.Hash( "lua" );
		foreach ( var entry in database.Entries.Values )
		{
			if ( entry.Type == sequenceType )
			{
				try
				{
					Walk( ScriptData.Read( database.Read( entry ) ), t =>
					{
						if ( t.Meta == "sound" && SequenceVisibility.Literal( t.Text( "event" ) ) is { } name ) Add( name );
					} );
				}
				catch ( InvalidDataException ) { }
			}
			else if ( entry.Type == luaType )
			{
				foreach ( Match match in Identifier.Matches( Encoding.Latin1.GetString( database.Read( entry ) ) ) ) Add( match.Value );
			}
		}

		var result = new Dictionary<uint, string>();
		foreach ( var name in names ) result.TryAdd( WwiseBank.Hash( name ), name );
		return result;
	}

	static void Walk( ScriptTable table, Action<ScriptTable> visit, HashSet<ScriptTable> seen = null )
	{
		seen ??= [];
		if ( !seen.Add( table ) ) return;
		visit( table );
		foreach ( var child in table.Children ) Walk( child, visit, seen );
	}

	/// <summary>Short bank name: <c>soundbanks/lvl_1wb_sfx</c> → <c>lvl_1wb_sfx</c>.</summary>
	public static string BankShortName( string bankPath ) => bankPath[(bankPath.LastIndexOf( '/' ) + 1)..];
}
