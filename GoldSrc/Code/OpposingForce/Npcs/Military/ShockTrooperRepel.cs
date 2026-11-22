using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Rappelling Shock Trooper" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_shocktrooper_repel", "strooper", MonsterClass.AlienMilitary, 0, 0, 0, 0,
	Mount = "opfor" )]
public sealed class ShockTrooperRepel : GruntRepel
{
	public ShockTrooperRepel() => GruntPrefab = "mount://opfor/entities/npcs/monster_shocktrooper.prefab";
}
