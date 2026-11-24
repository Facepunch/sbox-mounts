namespace Diesel;

enum UnitKind { Prop, Architecture, Character, Scripted }

static class UnitClassification
{
	public static UnitKind Classify( string path, XmlNode unit )
	{
		if ( unit.Attribute( "type" ) == "being" ) return UnitKind.Character;
		// This authored asset family contains animation-driven mission vehicles, often
		// with bind poses far from the origin. It is not a library of ordinary props.
		if ( path.StartsWith( "units/vehicles/animated_cars/", StringComparison.Ordinal ) )
			return UnitKind.Scripted;
		if ( path.StartsWith( "units/world/architecture/", StringComparison.Ordinal )
			|| path.Contains( "/backdrops/", StringComparison.Ordinal )
			|| path.Contains( "/backdrop_", StringComparison.Ordinal ) )
			return UnitKind.Architecture;
		return UnitKind.Prop;
	}
}
