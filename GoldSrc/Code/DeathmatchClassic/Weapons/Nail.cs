using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Hide]
public sealed class Nail : NpcProjectile
{
	internal override float Lifetime => 6;
	internal override string HitTag => "bullet";
	internal override void OnImpact( IDamageable victim, Vector3 position, Vector3 normal )
	{
		base.OnImpact( victim, position, normal );
		if ( victim is null ) ClassicExplosion.Mount?.NpcAudio.Emit( "weapons/tink1.wav", position );
	}
}
