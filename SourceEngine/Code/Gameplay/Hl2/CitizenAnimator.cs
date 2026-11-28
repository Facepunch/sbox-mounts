namespace Source1.Hl2;

[Title( "Citizen Animator" )]
public sealed class CitizenAnimator : NpcAnimator
{
	protected override NpcWeaponLayers ResolveWeaponLayers( Model model, ModelSequences sequences, BaseCombatWeapon weapon, Model worldModel, NpcWeaponLayers legs )
	{
		var idle = ActTable.RelaxedIdle( sequences, weapon, GameObject.Id.GetHashCode() );
		return idle is null ? legs : legs with { Clips = legs.Clips with { Idle = idle } };
	}
}
