namespace Diesel;

/// <summary>Literal, immediate geometry-only editable states. Never evaluates Diesel script expressions.</summary>
sealed class SequenceVisibility
{
	public Dictionary<string, bool> Objects { get; } = new( StringComparer.Ordinal );
	public Dictionary<string, bool> Bodies { get; } = new( StringComparer.Ordinal );

	public bool Matches( XmlNode obj )
	{
		var graphics = UnitGeometry.GraphicsObjects( obj ).Select( e => e.Attribute( "name" ) ).ToHashSet();
		var bodies = (obj.Element( "bodies" )?.Elements( "body" ) ?? []).Select( e => e.Attribute( "name" ) ).ToHashSet();
		return Objects.Keys.All( graphics.Contains ) && Bodies.Keys.All( bodies.Contains );
	}

	/// <param name="lenient">
	/// Runtime mode for mission-driven sequences (destruction, interaction): apply literal <c>object</c>/<c>body</c> toggles and
	/// follow literal <c>run_sequence</c> calls, skipping every action it can't reproduce (effects, sounds, materials, spawns, and
	/// anything conditional) instead of rejecting the whole sequence. Timing is ignored (final state). Null if nothing applied.
	/// The default strict mode is for placed mesh variations, where a partial state must never be shown.
	/// </param>
	public static SequenceVisibility Read( ScriptTable root, string variation, bool lenient = false )
	{
		var unit = root.Table( "unit" );
		if ( unit is null ) return null;
		var sequences = Ordered( unit ).Where( t => t.Meta == "sequence" ).ToArray();
		var result = new SequenceVisibility();
		var active = new HashSet<string>( StringComparer.Ordinal );
		int budget = 4096;
		if ( lenient ) return ApplyLenient( variation, 0 ) && result.Objects.Count + result.Bodies.Count > 0 ? result : null;
		return Apply( variation, 0 ) ? result : null;

		bool ApplyLenient( string name, int depth )
		{
			if ( depth > 32 || !active.Add( name ) ) return false;
			var sequence = sequences.FirstOrDefault( s => Literal( s.Text( "name" ) ) == name );
			if ( sequence is null ) { active.Remove( name ); return false; }
			foreach ( var action in Ordered( sequence ) )
			{
				if ( --budget < 0 ) break;
				// Conditional actions can't be evaluated; anything else with an expression target is skipped.
				if ( action.GetValueOrDefault( "condition" ) is string condition && condition.Trim() != "true" ) continue;
				if ( Literal( action.Text( "name" ) ) is not { } target ) continue;
				if ( action.Meta == "run_sequence" ) ApplyLenient( target, depth + 1 );
				else if ( action.Meta is "object" or "body" && Enabled( action.GetValueOrDefault( "enabled" ) ) is { } enabled )
					(action.Meta == "object" ? result.Objects : result.Bodies)[target] = enabled;
			}
			active.Remove( name );
			return true;
		}

		static bool? Enabled( object value ) => value switch
		{
			bool b => b,
			string s when s.Trim() == "true" => true,
			string s when s.Trim() == "false" => false,
			_ => null,
		};

		bool Apply( string name, int depth )
		{
			if ( depth > 32 || !active.Add( name ) ) return false;
			var sequence = sequences.FirstOrDefault( s => Literal( s.Text( "name" ) ) == name );
			if ( sequence is null ) return false;
			var actions = Ordered( sequence ).ToArray();
			// Serialized XML includes named aliases for the first action of each kind.
			if ( sequence.Any( p => !Numeric( p.Key ) && p.Key is not ("name" or "editable_state" or "reset_editable_state")
				&& !(p.Value is ScriptTable alias && actions.Contains( alias ) && p.Key == alias.Meta) ) ) return false;
			foreach ( var action in actions )
			{
				if ( --budget < 0 ) return false;
				string target = Literal( action.Text( "name" ) );
				if ( target is null ) return false;
				if ( action.Meta == "run_sequence" )
				{
					if ( action.Keys.Any( k => k != "name" ) || !Apply( target, depth + 1 ) ) return false;
				}
				else if ( action.Meta is "object" or "body" )
				{
					if ( action.Keys.Any( k => k is not ("name" or "enabled") ) || action.GetValueOrDefault( "enabled" ) is not bool enabled ) return false;
					(action.Meta == "object" ? result.Objects : result.Bodies)[target] = enabled;
				}
				else return false;
			}
			active.Remove( name );
			return true;
		}
	}

	static bool Numeric( string key ) => int.TryParse( key, out int index ) && index > 0;
	/// <summary>A script table's array part in index order (named aliases excluded).</summary>
	internal static IEnumerable<ScriptTable> Ordered( ScriptTable table ) => table
		.Where( p => Numeric( p.Key ) ).OrderBy( p => int.Parse( p.Key ) ).Select( p => p.Value ).OfType<ScriptTable>();

	/// <summary>The string inside a quoted Diesel literal expression such as <c> 'destroy_01' </c>, or null for real expressions.</summary>
	internal static string Literal( string expression )
	{
		string value = expression?.Trim();
		if ( value?.Length < 2 || value is null ) return null;
		char quote = value[0];
		return (quote == '\'' || quote == '"') && value[^1] == quote
			&& !value[1..^1].Contains( quote ) && !value.Contains( '\\' ) ? value[1..^1] : null;
	}
}
