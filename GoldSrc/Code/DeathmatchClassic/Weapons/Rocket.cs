using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Hide]
public sealed class Rocket : NpcProjectile
{
	internal override float Lifetime => 5;
	internal override string HitTag => "bullet";
	internal override void OnImpact( IDamageable victim, Vector3 position, Vector3 normal )
	{
		base.OnImpact( victim, position, normal );
		var origin = position - Velocity.Normal * 2;
		ClassicExplosion.Damage( GameObject, Shooter, origin, 120, victim );
		ClassicExplosion.Show( origin );
	}
}
