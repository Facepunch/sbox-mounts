namespace Diesel;

/// <summary>Separate editor visualization from runtime geometry using the unit's authored flags.</summary>
sealed class UnitGeometry
{
	public HashSet<ulong> Graphics { get; } = [];
	public HashSet<ulong> Collision { get; } = [];
	public Dictionary<ulong, string> CollisionTypes { get; } = [];
	public Dictionary<ulong, float> CollisionMasses { get; } = [];
	public Dictionary<ulong, string> CollisionMaterials { get; } = [];
	public Dictionary<ulong, float> CollisionShells { get; } = [];

	public static IEnumerable<XmlNode> GraphicsObjects( XmlNode obj )
	{
		var graphics = obj.Element( "graphics" );
		return (graphics?.Elements( "object" ) ?? []).Concat(
			(graphics?.Elements( "lod_object" ) ?? []).SelectMany( group => group.Elements( "object" ) ) );
	}

	public static UnitGeometry Read( XmlNode unit, XmlNode obj, SequenceVisibility variation = null )
	{
		var result = new UnitGeometry();
		if ( variation?.Matches( obj ) == false ) variation = null;
		var flags = (unit.Element( "extensions" )?.Elements( "extension" ) ?? [])
			.Where( e => e.Attribute( "name" ) == "unit_data" )
			.SelectMany( e => e.Elements( "var" ) )
			.Where( v => v.Attribute( "value" ) == "true" )
			.Select( v => v.Attribute( "name" ) ).ToHashSet();
		// Navigation splitters and other editor-only units have no runtime geometry at all.
		if ( flags.Contains( "only_exists_in_editor" ) ) return result;
		bool editorVisible = flags.Contains( "only_visible_in_editor" );

		if ( !editorVisible )
		{
			foreach ( var element in obj.Element( "graphics" )?.Elements( "object" ) ?? [] )
				if ( Enabled( element ) ) result.Graphics.Add( IdString.Hash( element.Attribute( "name" ) ) );

			foreach ( var group in obj.Element( "graphics" )?.Elements( "lod_object" ) ?? [] )
			{
				if ( group.Attribute( "enabled" ) == "false" ) continue;
				var levels = group.Elements( "object" ).Where( Enabled ).GroupBy( Lod ).OrderBy( level => level.Key );
				// Import one level, not overlapping near/far meshes. Distance switching is pending.
				foreach ( var element in levels.FirstOrDefault() ?? Enumerable.Empty<XmlNode>() )
					result.Graphics.Add( IdString.Hash( element.Attribute( "name" ) ) );
			}
		}

		foreach ( var body in obj.Element( "bodies" )?.Elements( "body" ) ?? [] )
		{
			// Editor picking shapes (e.g. light gizmos) and AI navigation helpers aren't physical world geometry.
			bool enabled = body.Attribute( "enabled" ) != "false";
			if ( body.Attribute( "name" ) is string bodyName && variation is not null ) enabled = variation.Bodies.GetValueOrDefault( bodyName, enabled );
			if ( !enabled || body.Attribute( "template" ) is "editor" or "nav_walk" ) continue;
			foreach ( var element in body.Elements( "object" ) )
				if ( element.Attribute( "collision_type" ) is not null && element.Attribute( "enabled" ) != "false"
					&& element.Attribute( "name" ) is string name )
				{
					ulong hash = IdString.Hash( name );
					result.Collision.Add( hash );
					result.CollisionTypes[hash] = element.Attribute( "collision_type" );
					// Mass is authored per collision object, in kilograms. Do not confuse
					// unit-level <mass><spawn .../> (massunit streaming) with physics mass.
					result.CollisionMasses.Remove( hash );
					if ( float.TryParse( element.Attribute( "mass" ), System.Globalization.NumberStyles.Float,
						System.Globalization.CultureInfo.InvariantCulture, out float mass ) && float.IsFinite( mass ) && mass > 0 )
						result.CollisionMasses[hash] = mass;
					result.CollisionMaterials.Remove( hash );
					if ( element.Attribute( "material" ) is { Length: > 0 } material )
						result.CollisionMaterials[hash] = material;
					result.CollisionShells.Remove( hash );
					if ( PhysicsMaterials.Positive( element.Attribute( "shell" ) ) is float shell )
						result.CollisionShells[hash] = shell;
				}
		}
		return result;

		bool Enabled( XmlNode element ) => element.Attribute( "name" ) is string name
			&& (variation?.Objects.GetValueOrDefault( name, element.Attribute( "enabled" ) != "false" ) ?? element.Attribute( "enabled" ) != "false");
	}

	static int Lod( XmlNode element ) => int.TryParse( element.Attribute( "lod" ),
		System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int level ) && level >= 0 ? level : 0;
}
