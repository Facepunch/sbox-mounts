namespace Diesel;

/// <summary>Authored physical debris bodies and their bound graphic/collision objects.</summary>
static class DebrisDefinition
{
	public sealed record Body( string Name, HashSet<ulong> Objects, float Mass );
	public static Body[] Read( XmlNode obj ) => (obj.Element( "bodies" )?.Elements( "body" ) ?? [])
		.Where( b => b.Attribute( "template" ) == "debris" && b.Attribute( "enabled" ) != "false" )
		.Select( b => new Body( b.Attribute( "name" ),
			b.Elements( "object" ).Where( o => o.Attribute( "enabled" ) != "false" && o.Attribute( "name" ) is not null )
				.Select( o => IdString.Hash( o.Attribute( "name" ) ) ).ToHashSet(),
			b.Elements( "object" ).Where( o => o.Attribute( "collision_type" ) is not null )
				.Sum( o => EffectDefinition.Number( o, "mass", 1 ) ) ) )
		.Where( b => !string.IsNullOrEmpty( b.Name ) && b.Objects.Count > 0 ).ToArray();
}
