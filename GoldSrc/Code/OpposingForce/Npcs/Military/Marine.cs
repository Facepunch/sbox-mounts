using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Marine" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_human_grunt_ally", "hgrunt_opfor", MonsterClass.PlayerAlly, 50, 16, 72, 160,
	Mount = "opfor", Attack = NpcAttack.Bullet, Damage = 5, Range = 2048, ProjectileModel = "grenade", StrikeEvents = new[] { 4, 5, 6 }, Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class Marine : AlliedGrunt
{
}
