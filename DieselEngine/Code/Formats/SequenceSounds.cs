namespace Diesel;

/// <summary>
/// Sound events a unit's sequence plays: literal <c>sound { action='play', event='window_large_shatter' }</c> actions, following
/// literal <c>run_sequence</c> calls. Conditional actions and expression-valued events are skipped (never evaluated).
/// </summary>
static class SequenceSounds
{
	/// <summary>Event names played by <paramref name="sequence"/>, in action order, without duplicates.</summary>
	public static List<string> Read( ScriptTable root, string sequence )
	{
		var result = new List<string>();
		if ( root?.Table( "unit" ) is not { } unit || string.IsNullOrEmpty( sequence ) ) return result;
		var sequences = SequenceVisibility.Ordered( unit ).Where( t => t.Meta == "sequence" ).ToArray();
		var active = new HashSet<string>( StringComparer.Ordinal );
		int budget = 2048;
		Collect( sequence, 0 );
		return result;

		void Collect( string name, int depth )
		{
			if ( depth > 16 || !active.Add( name ) ) return;
			var table = sequences.FirstOrDefault( s => SequenceVisibility.Literal( s.Text( "name" ) ) == name );
			if ( table is not null )
				foreach ( var action in SequenceVisibility.Ordered( table ) )
				{
					if ( --budget < 0 ) break;
					if ( action.GetValueOrDefault( "condition" ) is string condition && condition.Trim() != "true" ) continue;
					if ( action.Meta == "run_sequence" && SequenceVisibility.Literal( action.Text( "name" ) ) is { } target ) Collect( target, depth + 1 );
					else if ( action.Meta == "sound" && IsPlay( action ) && SequenceVisibility.Literal( action.Text( "event" ) ) is { } eventName
						&& !result.Contains( eventName, StringComparer.OrdinalIgnoreCase ) )
						result.Add( eventName );
				}
			active.Remove( name );
		}
	}

	static bool IsPlay( ScriptTable action ) => action.Text( "action" ) is not { } verb || SequenceVisibility.Literal( verb ) == "play";

	/// <summary>Literal names of every sequence in the manager.</summary>
	public static IEnumerable<string> SequenceNames( ScriptTable root ) => root?.Table( "unit" ) is { } unit
		? SequenceVisibility.Ordered( unit ).Where( t => t.Meta == "sequence" ).Select( t => SequenceVisibility.Literal( t.Text( "name" ) ) ).Where( n => n is not null )
		: [];

	/// <summary>
	/// A door unit's opening and closing sounds: the first sound played by a sequence whose name says open (<c>open</c>,
	/// <c>open_in</c>, <c>anim_open_door</c>…) or close. Exact names are preferred over longer matches.
	/// </summary>
	public static (string Open, string Close) DoorSounds( ScriptTable root )
	{
		var names = SequenceNames( root ).ToList();
		return (First( "open" ), First( "close" ));

		string First( string word )
		{
			foreach ( var name in names.Where( n => Matches( n, word ) ).OrderBy( n => n == word ? 0 : n.StartsWith( word, StringComparison.Ordinal ) ? 1 : 2 ).ThenBy( n => n.Length ) )
				if ( Read( root, name ).FirstOrDefault() is { } sound ) return sound;
			return null;
		}

		static bool Matches( string name, string word )
		{
			string other = word == "open" ? "close" : "open";
			return name.Contains( word, StringComparison.OrdinalIgnoreCase ) && !name.Contains( other, StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "lock", StringComparison.OrdinalIgnoreCase ) && !name.Contains( "explo", StringComparison.OrdinalIgnoreCase );
		}
	}
}
