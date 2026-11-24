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

	public static SequenceVisibility Read( ScriptTable root, string variation )
	{
		var unit = root.Table( "unit" );
		if ( unit is null ) return null;
		var sequences = Ordered( unit ).Where( t => t.Meta == "sequence" ).ToArray();
		var result = new SequenceVisibility();
		var active = new HashSet<string>( StringComparer.Ordinal );
		int budget = 4096;
		return Apply( variation, 0 ) ? result : null;

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
	static IEnumerable<ScriptTable> Ordered( ScriptTable table ) => table
		.Where( p => Numeric( p.Key ) ).OrderBy( p => int.Parse( p.Key ) ).Select( p => p.Value ).OfType<ScriptTable>();

	static string Literal( string expression )
	{
		string value = expression?.Trim();
		if ( value?.Length < 2 || value is null ) return null;
		char quote = value[0];
		return (quote == '\'' || quote == '"') && value[^1] == quote
			&& !value[1..^1].Contains( quote ) && !value.Contains( '\\' ) ? value[1..^1] : null;
	}
}
