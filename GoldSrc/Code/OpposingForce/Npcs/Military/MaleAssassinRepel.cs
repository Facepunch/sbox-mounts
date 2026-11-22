using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Rappelling Male Assassin" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_assassin_repel", "massn", MonsterClass.HumanMilitary, 0, 0, 0, 0,
	Mount = "opfor" )]
public sealed class MaleAssassinRepel : GruntRepel
{
	public MaleAssassinRepel() => GruntPrefab = "mount://opfor/entities/npcs/monster_male_assassin.prefab";
}
