using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Rappelling Marine Engineer" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_torch_ally_repel", "hgrunt_torch", MonsterClass.PlayerAlly, 0, 0, 0, 0,
	Mount = "opfor" )]
public sealed class TorchRepel : GruntRepel
{
	public TorchRepel() => GruntPrefab = "mount://opfor/entities/npcs/monster_human_torch_ally.prefab";
}
