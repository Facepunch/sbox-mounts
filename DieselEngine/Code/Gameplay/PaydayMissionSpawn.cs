namespace Diesel;

/// <summary>Marks an NPC spawned by a mission element, so ElementEnemyDummyTrigger can react to what it does.</summary>
[Hide]
public sealed class PaydayMissionSpawn : Component
{
	/// <summary>The ElementSpawnCivilian/ElementSpawnEnemyDummy that produced this NPC.</summary>
	[Property] public int ElementId { get; set; }
}
