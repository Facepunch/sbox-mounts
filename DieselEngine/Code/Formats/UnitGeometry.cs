namespace Diesel;

/// <summary>Separate editor visualization from runtime geometry using the unit's authored flags.</summary>
sealed class UnitGeometry
{
	public HashSet<ulong> Graphics { get; } = [];
	public HashSet<ulong> Collision { get; } = [];

	public static UnitGeometry Read( XmlNode unit, XmlNode obj )
	{
		var result = new UnitGeometry();
		bool editorVisible = (unit.Element( "extensions" )?.Elements( "extension" ) ?? [])
			.Where( e => e.Attribute( "name" ) == "unit_data" )
			.SelectMany( e => e.Elements( "var" ) )
			.Any( v => v.Attribute( "name" ) == "only_visible_in_editor" && v.Attribute( "value" ) == "true" );

		if ( !editorVisible )
		{
			foreach ( var element in obj.Element( "graphics" )?.Elements( "object" ) ?? [] )
				if ( element.Attribute( "enabled" ) != "false" && element.Attribute( "name" ) is string name )
					result.Graphics.Add( IdString.Hash( name ) );
		}

		foreach ( var body in obj.Element( "bodies" )?.Elements( "body" ) ?? [] )
		{
			// Editor picking shapes (e.g. light gizmos) and AI navigation helpers aren't physical world geometry.
			if ( body.Attribute( "enabled" ) == "false" || body.Attribute( "template" ) is "editor" or "nav_walk" ) continue;
			foreach ( var element in body.Elements( "object" ) )
				if ( element.Attribute( "collision_type" ) is not null && element.Attribute( "enabled" ) != "false"
					&& element.Attribute( "name" ) is string name )
					result.Collision.Add( IdString.Hash( name ) );
		}
		return result;
	}
}
