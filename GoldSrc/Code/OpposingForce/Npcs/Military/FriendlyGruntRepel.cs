using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Rappelling Friendly Grunt" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_fgrunt_repel", "hgrunt", MonsterClass.PlayerAlly, 0, 0, 0, 0,
	Mount = "opfor" )]
public sealed class FriendlyGruntRepel : GruntRepel
{
	public FriendlyGruntRepel() => GruntPrefab = "mount://opfor/entities/npcs/monster_human_friendly_grunt.prefab";
}
