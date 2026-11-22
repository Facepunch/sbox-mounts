using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Rappelling Marine" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_grunt_ally_repel", "hgrunt_opfor", MonsterClass.PlayerAlly, 0, 0, 0, 0,
	Mount = "opfor" )]
public sealed class MarineRepel : GruntRepel
{
	public MarineRepel() => GruntPrefab = "mount://opfor/entities/npcs/monster_human_grunt_ally.prefab";
}
