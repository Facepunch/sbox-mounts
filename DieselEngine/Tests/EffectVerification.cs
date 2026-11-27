using Diesel;
using System.Text;

static class EffectVerification
{
	public static void Run( BundleDatabase db )
	{
		var names = Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' ).Distinct();
		var definitions = names.Where( n => db.Entries.ContainsKey( (IdString.Hash( "effect" ), IdString.Hash( n )) ) )
			.Select( n => new EffectDefinition( db.Find( "effect", n ) ) ).ToArray();
		if ( definitions.Length != 209 ) throw new Exception( "Original effect inventory changed." );
		var textures = definitions.SelectMany( d => d.Textures ).Distinct().ToArray();
		foreach ( var texture in textures )
			if ( db.Find( "texture", texture ) is not { Length: > 128 } bytes || BitConverter.ToUInt32( bytes, 0 ) != 0x20534444 )
				throw new Exception( $"Missing or malformed effect DDS: {texture}" );
		var curve = XmlNode.Parse( "<node><keys loop='false'><key t='0' v='1'/><key t='1' v='3'/></keys></node>" );
		if ( EffectDefinition.Curve( curve, "keys", .5f, default ).x != 2 ) throw new Exception( "Curve interpolation failed." );
		try { EffectDefinition.Number( XmlNode.Parse( "<node value='NaN'/>" ), "value" ); throw new Exception( "Accepted nonfinite parameter." ); }
		catch ( InvalidDataException ) { }
		const string planks = "units/world/architecture/secret_stash/props/secret_stash_planks_outline_no_sunray";
		var actions = SequenceEffects.Read( ScriptData.Read( db.Find( "sequence_manager", planks ) ), "destroy_planks" );
		if ( actions.Count != 2 || actions[0].Kind != "effect" || actions[1].Kind != "spawn_unit" || actions.Any( a => a.Anchor != "e_effect" ) )
			throw new Exception( "Plank destruction action/attachment parsing failed." );
		var unit = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "unit", actions[1].Path ) ) );
		var bodies = DebrisDefinition.Read( XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "object", unit.Element( "object" ).Attribute( "file" ) ) ) ) );
		if ( bodies.Length != 5 || bodies.Any( b => b.Objects.Count != 2 ) ) throw new Exception( "Authored plank body split failed." );
		Console.WriteLine( $"PASS: {definitions.Length} original effects, {textures.Length} DDS dependencies, curves, sequence anchors and five authored debris bodies." );
	}
}
