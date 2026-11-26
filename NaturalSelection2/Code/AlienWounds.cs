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

	internal static TimeUntil Flinch( Component.IDamageable.DamageEvent hit, float damage, float maxHealth, TimeUntil end )
	{
		float intensity = Math.Clamp( Intensity( end ) + damage / (maxHealth * 0.2f), 0.2f, 1 );
		// Spark adds the live weapon's bonus on impact, then caps intensity on the next update.
		if ( hit.DamageInfo.Tags.Has( ExtraFlinch ) )
			intensity = Math.Min( intensity + 0.1f, 1 + Decay * Time.Delta );
		return intensity / Decay;
	}

	internal float ApplyDamage( Component.IDamageable.DamageEvent hit, float armor, float maxHealth, float maxArmor, float armorOnlyDamage = 0 )
	{
		float ratio = AlienArmor.HealthPerArmor( hit.DamageInfo );
		float armorLost = ratio == 0 ? 0 : Math.Min( armor, (armorOnlyDamage + hit.AcceptedDamage * (0.7f / 0.3f)) / ratio );
		flinchEnd = Flinch( hit, hit.AcceptedDamage + armorLost * ratio, maxHealth, flinchEnd );
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
