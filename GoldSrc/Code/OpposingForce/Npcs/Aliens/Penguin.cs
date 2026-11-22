using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Penguin" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_penguin", "w_penguin", MonsterClass.AlienWeapon, 2, 4, 8, 300,
	Mount = "opfor", Movement = NpcMovement.Bouncing, Damage = 10, IdleSequence = "run", Blood = BloodColor.Yellow )]
public sealed class Penguin : Snark
{
	internal override float InitialBlastDamage => 100;
	internal override float ChargeBite( float damage ) => System.MathF.Min( damage + 100, 500 );
}
