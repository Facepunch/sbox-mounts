using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Male Assassin" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_male_assassin", "massn", MonsterClass.HumanMilitary, 50, 16, 72, 160,
	Mount = "opfor", Attack = NpcAttack.Bullet, Damage = 5, Range = 2048, ProjectileModel = "grenade", StrikeEvents = new[] { 4, 5, 6 }, Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class MaleAssassin : HumanGrunt
{
}
