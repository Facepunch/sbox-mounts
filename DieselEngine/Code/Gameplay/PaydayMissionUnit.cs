namespace Diesel;

/// <summary>
/// Marks a placed world unit that the level's mission script refers to by <c>unit_id</c> (disable-unit,
/// unit-sequence and sequence-trigger elements), with what's needed to rebuild it in another sequence state.
/// </summary>
[Hide]
public sealed class PaydayMissionUnit : Component
{
	[Property] public int Id { get; set; }
	[Property] public string Unit { get; set; }
	[Property] public string MaterialVariation { get; set; }
	/// <summary>Last sequence the mission ran on this unit, if any.</summary>
	[Property] public string Sequence { get; set; }
}
