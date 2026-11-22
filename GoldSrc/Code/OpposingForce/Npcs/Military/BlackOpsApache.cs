using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Black Ops Apache" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_blkop_apache", "blkop_apache", MonsterClass.HumanMilitary, 250, 52, 52, 265,
	Mount = "opfor", MetalBody = true, Attack = NpcAttack.Bullet, Damage = 10, Range = 4092, Movement = NpcMovement.Flying, ProjectileModel = "hvr" )]
public sealed class BlackOpsApache : Apache
{
}
