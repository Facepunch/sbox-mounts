namespace Diesel;

/// <summary>
/// A unit's own damage rules from its sequence manager: <c>unit { body { name, endurance { bullet, explosion, melee,
/// run_sequence { name } }… } }</c>. Each body has ordered, cumulative stages. When a body's accumulated damage of a type
/// reaches the stage's threshold for that type, the stage's sequences run, once. E.g. Bank's large window cracks
/// (<c>destroy_01</c>) at bullet 1 and shatters (<c>destroy_02</c>, which also disables body <c>window3</c>) at bullet 2.
/// Thresholds are in source damage units. 995 bodies across 219 of 492 managers carry endurance.
/// </summary>
sealed class UnitEndurance
{
	public sealed record Stage( Dictionary<string, float> Thresholds, string[] Sequences );

	/// <summary>Endurance stages per body name.</summary>
	public Dictionary<string, List<Stage>> Bodies { get; } = new( StringComparer.Ordinal );
	/// <summary>Bodies enabled in the unit's default state (object XML <c>enabled</c> attribute).</summary>
	public HashSet<string> DefaultEnabledBodies { get; } = new( StringComparer.Ordinal );
	/// <summary>The sequence manager script, for evaluating sequences' body toggles.</summary>
	public ScriptTable Manager { get; init; }

	public static UnitEndurance Read( ScriptTable manager, XmlNode obj )
	{
		var unit = manager?.Table( "unit" );
		if ( unit is null ) return null;
		var result = new UnitEndurance { Manager = manager };
		foreach ( var body in obj?.Element( "bodies" )?.Elements( "body" ) ?? [] )
			if ( body.Attribute( "name" ) is { } name && body.Attribute( "enabled" )?.Trim() != "false" ) result.DefaultEnabledBodies.Add( name );
		foreach ( var body in SequenceVisibility.Ordered( unit ).Where( t => t.Meta == "body" ) )
		{
			if ( SequenceVisibility.Literal( body.Text( "name" ) ) is not { } name ) continue;
			var stages = new List<Stage>();
			foreach ( var endurance in SequenceVisibility.Ordered( body ).Where( t => t.Meta == "endurance" ) )
			{
				var thresholds = endurance.Where( p => p.Value is float f && float.IsFinite( f ) && f >= 0 )
					.ToDictionary( p => p.Key, p => (float)p.Value, StringComparer.Ordinal );
				var sequences = SequenceVisibility.Ordered( endurance ).Where( a => a.Meta == "run_sequence" )
					.Select( a => SequenceVisibility.Literal( a.Text( "name" ) ) ).Where( n => n is not null ).ToArray();
				if ( thresholds.Count > 0 && sequences.Length > 0 ) stages.Add( new( thresholds, sequences ) );
			}
			if ( stages.Count > 0 ) result.Bodies[name] = stages;
		}
		return result.Bodies.Count > 0 ? result : null;
	}

	/// <summary>Body enable/disable toggles a sequence applies (lenient: literal actions only, following run_sequence).</summary>
	public IReadOnlyDictionary<string, bool> BodyToggles( string sequence ) =>
		SequenceVisibility.Read( Manager, sequence, lenient: true )?.Bodies ?? new Dictionary<string, bool>();
}
