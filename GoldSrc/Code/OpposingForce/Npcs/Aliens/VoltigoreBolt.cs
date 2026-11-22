using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Hide]
public sealed class VoltigoreBolt : ElectricBolt
{
	internal override void OnImpact( IDamageable victim, Vector3 position, Vector3 normal )
	{
		base.OnImpact( victim, position, normal );
		Blast.DamageInRadius( GameObject, Shooter, position, Damage, 128, "shock", "alwaysgib" );
	}
}
