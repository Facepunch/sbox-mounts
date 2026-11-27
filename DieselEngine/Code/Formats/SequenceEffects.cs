using System.Text.RegularExpressions;

namespace Diesel;

/// <summary>Literal effect/debris actions only; arbitrary Diesel expressions are never executed.</summary>
static class SequenceEffects
{
	public sealed record Action( string Kind, string Path, string Anchor, Vector3 Offset );
	public static List<Action> Read( ScriptTable root, string sequence )
	{
		var result = new List<Action>();
		if ( root?.Table( "unit" ) is not { } unit ) return result;
		var sequences = SequenceVisibility.Ordered( unit ).Where( s => s.Meta == "sequence" ).ToArray();
		var active = new HashSet<string>();
		int budget = 2048;
		Visit( sequence, 0 );
		return result;
		void Visit( string name, int depth )
		{
			if ( depth > 16 || !active.Add( name ) ) return;
			var table = sequences.FirstOrDefault( s => SequenceVisibility.Literal( s.Text( "name" ) ) == name );
			if ( table is not null ) foreach ( var action in SequenceVisibility.Ordered( table ) )
			{
				if ( --budget < 0 ) break;
				if ( action.GetValueOrDefault( "condition" ) is { } condition && condition.ToString().Trim() != "true" ) continue;
				if ( action.ContainsKey( "delay" ) || action.ContainsKey( "start_time" ) ) continue;
				var target = SequenceVisibility.Literal( action.Text( "name" ) );
				if ( target is null ) continue;
				if ( action.Meta == "run_sequence" ) { Visit( target, depth + 1 ); continue; }
				if ( action.Meta is not ("effect" or "spawn_unit") ) continue;
				var parent = action.Text( "parent" );
				var position = action.Text( "position" );
				var rotation = action.Text( "rotation" );
				string anchor = null;
				Vector3 offset = default;
				if ( parent is not null && (anchor = ObjectCall( parent, "object" )) is null ) continue;
				if ( position is not null )
				{
					if ( ObjectCall( position, "object_pos" ) is { } obj ) anchor = obj;
					else if ( !Offset( position, out offset ) ) continue;
				}
				if ( rotation is not null && ObjectCall( rotation, "object_rot" ) != anchor ) continue;
				result.Add( new( action.Meta, target, anchor, offset ) );
			}
			active.Remove( name );
		}
	}
	static string ObjectCall( string text, string function )
	{
		var match = Regex.Match( text ?? "", @"^\s*" + function + @"\(\s*'([^']+)'\s*\)\s*$" );
		return match.Success ? match.Groups[1].Value : null;
	}
	static bool Offset( string text, out Vector3 offset )
	{
		offset = default;
		var match = Regex.Match( text, @"^\s*v\(\s*([-+0-9.eE,\s]*)\)\s*$" );
		if ( !match.Success ) return false;
		var parts = match.Groups[1].Value.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
		if ( parts.Length == 0 ) return true;
		if ( parts.Length != 3 ) return false;
		var values = new float[3];
		for ( int i = 0; i < 3; i++ )
			if ( !float.TryParse( parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i] )
				|| !float.IsFinite( values[i] ) ) return false;
		offset = Coordinates.Position( new Vector3( values[0], values[1], values[2] ) );
		return true;
	}
}
