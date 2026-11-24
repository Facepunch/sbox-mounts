namespace Diesel;

/// <summary>Initial authored light state; mission-driven switching and baked projection maps are not replayed.</summary>
sealed class MapLights( BundleDatabase database )
{
	readonly Dictionary<string, Dictionary<string, LightDefinition>> definitions = new( StringComparer.Ordinal );
	readonly Dictionary<string, float> intensities = LoadIntensities( database );
	public int Count { get; private set; }

	static Dictionary<string, float> LoadIntensities( BundleDatabase database )
	{
		var bytes = database.Find( "light_intensities", "core/settings/light_intensities" );
		return bytes is null ? new() { ["identity"] = 1, ["none"] = 0 } : UnitLights.ReadIntensities( bytes );
	}

	public void Add( ScriptTable unit, GameObject parent )
	{
		if ( unit.Table( "lights" )?.Count is not > 0 ) return;
		string name = unit.Text( "name" );
		if ( !definitions.TryGetValue( name, out var lights ) )
		{
			lights = [];
			try
			{
				var unitBytes = database.Find( "unit", name );
				string path = unitBytes is null ? null : UnitModel.Xml( unitBytes ).Element( "object" )?.Attribute( "file" );
				if ( path is not null && database.Find( "object", path ) is { } obj && database.Find( "model", path ) is { } model )
					lights = UnitLights.Read( UnitModel.Xml( obj ), new ModelFile( model ) );
			}
			catch ( Exception e ) { Log.Warning( $"PAYDAY lights {name}: {e.Message}" ); }
			definitions[name] = lights;
		}
		foreach ( var placement in UnitLights.Place( unit, lights, intensities ) )
		{
			var node = placement.Definition.Node;
			var go = new GameObject( true, $"{unit.Text( "name_id" ) ?? name}/{placement.Definition.Name}" ) { Parent = parent, IsStatic = true };
			go.Tags.Add( "diesel_light" );
			var position = unit.GetValueOrDefault( "position" ) is Vector3 p ? Coordinates.Position( p ) : Vector3.Zero;
			var rotation = unit.GetValueOrDefault( "rotation" ) is Rotation r ? Coordinates.Rotation( r ) : Rotation.Identity;
			go.LocalPosition = position + rotation * node.Position;
			go.LocalRotation = rotation * Rotation.LookAt( node.Direction, node.Up );
			Light light;
			if ( node.Spot )
			{
				var spot = go.AddComponent<SpotLight>();
				spot.Radius = placement.Range;
				spot.ConeInner = placement.InnerAngle;
				spot.ConeOuter = placement.OuterAngle;
				light = spot;
			}
			else
			{
				var point = go.AddComponent<PointLight>();
				point.Radius = placement.Range;
				light = point;
			}
			light.LightColor = new Color( placement.Color.x, placement.Color.y, placement.Color.z );
			light.Shadows = placement.Shadows;
			Count++;
		}
	}
}
