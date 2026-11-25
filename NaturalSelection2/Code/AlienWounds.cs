using System;

namespace NS2;

internal sealed class AlienWounds
{
	float intensity, damageFraction;
	TimeUntil nextFlinch = 0.3f;

	internal float ApplyDamage( Component.IDamageable.DamageEvent hit, float armor, float maxHealth, float maxArmor )
	{
		float ratio = AlienArmor.HealthPerArmor( hit.DamageInfo );
		float healthLost = hit.AcceptedDamage;
		float armorLost = ratio == 0 ? 0 : Math.Min( armor, healthLost * (0.7f / 0.3f) / ratio );

		// NS2 accumulates intensity from incoming damage and wound severity from effective health lost.
		float damage = healthLost + armorLost * ratio;
		damageFraction += (healthLost + armorLost * 2) / (maxHealth + maxArmor * 2);
		intensity = Math.Clamp( intensity + damage / (maxHealth * 0.2f), 0.2f, 1 );
		return armorLost;
	}

	internal string Update()
	{
		intensity = Math.Max( 0, intensity - 0.4f * Time.Delta );
		if ( nextFlinch > 0 ) return null;
		nextFlinch = 0.3f;
		if ( damageFraction <= 0 ) return null;
		var cue = damageFraction > 0.35f || intensity > 0.35f ? "wound_serious" : "wound";
		damageFraction = 0;
		return cue;
	}
}
