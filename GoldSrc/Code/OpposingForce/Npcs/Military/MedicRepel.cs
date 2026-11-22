using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Rappelling Marine Medic" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_medic_ally_repel", "hgrunt_medic", MonsterClass.PlayerAlly, 0, 0, 0, 0,
	Mount = "opfor" )]
public sealed class MedicRepel : GruntRepel
{
	public MedicRepel() => GruntPrefab = "mount://opfor/entities/npcs/monster_human_medic_ally.prefab";
}
