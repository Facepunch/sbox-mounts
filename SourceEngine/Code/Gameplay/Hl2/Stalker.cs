using Sandbox.Npcs;

namespace Source1.Hl2;

public sealed class Stalker : CreatureNpc
{
	// A lone stalker leaves players and their allies alone.
	protected override void SetupRelationships() { }

	protected override Relationship GetFallbackRelationship( GameObject other ) =>
		other.Tags.Has( "antlion" ) || other.GetComponent<Vortigaunt>().IsValid()
			? new( Disposition.Hostile ) : base.GetFallbackRelationship( other );
}
