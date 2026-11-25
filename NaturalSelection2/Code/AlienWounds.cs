using System;

namespace NS2;

internal sealed class AlienWounds
{
	TimeUntil flinchEnd;
	float damageFraction;
	TimeUntil nextFlinch = 0.3f;

	const float Decay = 0.4f;
	internal const string ExtraFlinch = "ns2_extra_flinch";
	internal static float Intensity( TimeUntil end ) => Math.Clamp( end.Relative * Decay, 0, 1 );

	internal static (float Armor, TimeUntil End) ApplyFlinch( Component.IDamageable.DamageEvent hit, float armor, float maxHealth, TimeUntil end )
	{
		float ratio = AlienArmor.HealthPerArmor( hit.DamageInfo );
		float healthLost = hit.AcceptedDamage;
		float armorLost = ratio == 0 ? 0 : Math.Min( armor, healthLost * (0.7f / 0.3f) / ratio );
		float damage = healthLost + armorLost * ratio;
		float intensity = Math.Clamp( Intensity( end ) + damage / (maxHealth * 0.2f), 0.2f, 1 );
		// Spark adds the live weapon's bonus on impact, then caps intensity on the next update.
		if ( hit.DamageInfo.Tags.Has( ExtraFlinch ) )
			intensity = Math.Min( intensity + 0.1f, 1 + Decay * Time.Delta );
		return (armor - armorLost, intensity / Decay);
	}

	internal float ApplyDamage( Component.IDamageable.DamageEvent hit, float armor, float maxHealth, float maxArmor )
	{
		var (remaining, end) = ApplyFlinch( hit, armor, maxHealth, flinchEnd );
		flinchEnd = end;
		float armorLost = armor - remaining;
		damageFraction += (hit.AcceptedDamage + armorLost * 2) / (maxHealth + maxArmor * 2);
		return armorLost;
	}

	internal string Update()
	{
		if ( nextFlinch > 0 ) return null;
		nextFlinch = 0.3f;
		if ( damageFraction <= 0 ) return null;
		var cue = damageFraction > 0.35f || Intensity( flinchEnd ) > 0.35f ? "wound_serious" : "wound";
		damageFraction = 0;
		return cue;
	}
}
