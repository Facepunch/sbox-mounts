using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Zombie Security Guard" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_zombie_barney", "zombie_barney", MonsterClass.AlienMonster, 50, 16, 72, 72.48f,
	Mount = "opfor", Attack = NpcAttack.Melee, Damage = 20, Range = 64, Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class ZombieSecurityGuard : Zombie
{
}
