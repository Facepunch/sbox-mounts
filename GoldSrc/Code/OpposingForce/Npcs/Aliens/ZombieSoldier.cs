using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Zombie Soldier" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_zombie_soldier", "zombie_soldier", MonsterClass.AlienMonster, 60, 16, 72, 72.48f,
	Mount = "opfor", Attack = NpcAttack.Melee, Damage = 20, Range = 64, Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class ZombieSoldier : Zombie
{
}
