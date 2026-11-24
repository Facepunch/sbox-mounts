namespace Diesel;

sealed record LightDefinition( string Name, ModelFile.LightNode Node, bool Enabled, bool Shadows );
sealed record LightPlacement( LightDefinition Definition, Vector3 Color, float Range, float InnerAngle, float OuterAngle, bool Shadows );

static class UnitLights
{
	public static Dictionary<string, LightDefinition> Read( XmlNode obj, ModelFile model )
	{
		string root = obj.Element( "diesel" )?.Attribute( "orientation_object" );
		var nodes = model.ReadLights( root is null ? 0 : IdString.Hash( root ) ).ToDictionary( n => n.Name );
		var result = new Dictionary<string, LightDefinition>( StringComparer.Ordinal );
		foreach ( var light in obj.Element( "lights" )?.Elements( "light" ) ?? [] )
		{
			string name = light.Attribute( "name" );
			if ( name is null || !nodes.TryGetValue( IdString.Hash( name ), out var node ) ) continue;
			result[name] = new( name, node, light.Attribute( "enabled" ) != "false",
				light.Attribute( "projection" ) == "true" || (light.Attribute( "type" ) ?? "").Contains( "shadow", StringComparison.Ordinal ) );
		}
		return result;
	}

	public static IEnumerable<LightPlacement> Place( ScriptTable unit, Dictionary<string, LightDefinition> definitions, Dictionary<string, float> intensities )
	{
		foreach ( var light in unit.Table( "lights" )?.Children ?? [] )
		{
			string name = light.Text( "name" );
			if ( name is null || !definitions.TryGetValue( name, out var definition ) || !light.Flag( "enabled", definition.Enabled ) ) continue;
			float range = light.Number( "far_range" );
			float multiplier = LightIntensities.Resolve( intensities, light.Text( "multiplier" ) ?? "identity" );
			var color = light.GetValueOrDefault( "color" ) is Vector3 c ? c : Vector3.One;
			if ( !float.IsFinite( range ) || range <= 0 || multiplier <= 0
				|| !float.IsFinite( color.x + color.y + color.z ) ) continue;
			float outer = light.Number( "spot_angle_end", 45 ), inner = light.Number( "spot_angle_start", 0 );
			if ( !float.IsFinite( outer ) || !float.IsFinite( inner ) ) continue;
			// Diesel stores full cone angles; s&box exposes half angles.
			outer = Math.Clamp( outer * 0.5f, 0.5f, 89.5f );
			inner = Math.Clamp( inner * 0.5f, 0, outer );
			yield return new( definition, color * multiplier, range * Coordinates.Scale, inner, outer,
				definition.Shadows && !unit.Flag( "disable_shadows" ) );
		}
	}
}
