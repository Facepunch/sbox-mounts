using System;

namespace NS2;

internal static class SupportDamage
{
	internal const float ClusterBurnDamage = 8f * 2.1f * 2.5f;

	// MACs receive structural damage, Drifters are small targets, and only Hydras are flammable.
	internal static void Absorb( Component.IDamageable.DamageEvent hit, Component victim, float armor )
	{
		var tags = hit.DamageInfo.Tags;
		bool structural = victim is not Drifter;
		float damage = hit.AcceptedDamage;
		if ( tags.HasAny( "gas", "biological" ) || (!structural && tags.HasAny( "structuresonly", "splash" )) ||
			(victim is Mac && tags.Has( "nervegas" )) )
			damage = 0;
		else if ( tags.HasAny( "clusterflame", "clusterflamefragment" ) )
		{
			if ( structural ) damage *= 2.875f;
			if ( damage > 0 && victim is Hydra && tags.Has( "clusterflame" ) ) damage += ClusterBurnDamage;
		}
		else if ( structural && tags.Has( "grenadelauncher" ) ) damage *= 4;
		else if ( structural && tags.HasAny( "flame", "door", "structural", "structuralheavy" ) )
			damage *= victim is Hydra && tags.Has( "flame" ) ? 5 : 2;
		else if ( !structural && tags.Has( "spreading" ) ) damage *= 0.75f;

		if ( tags.HasAny( "bullet", "buckshot", "railgun", "grenade" ) && victim.GetComponent<UmbraProtection>().IsValid() )
			damage *= 0.8f;

		var (ratio, fraction) = ArmorRule( hit.DamageInfo, victim );
		if ( damage <= 0 || (fraction == 1 && armor <= 0) ) hit.Cancelled = true;
		hit.AcceptedDamage = fraction == 1 ? 0 : damage - Math.Min( armor * ratio, damage * fraction );
	}

	internal static (float Armor, TimeUntil End) Apply( Component.IDamageable.DamageEvent hit, Component victim, float armor, float maxHealth, TimeUntil end )
	{
		var (ratio, fraction) = ArmorRule( hit.DamageInfo, victim );
		float lost = fraction == 1
			? Math.Min( armor, (hit.DamageInfo.Damage > 0 ? hit.DamageInfo.Damage : 0) / ratio )
			: Math.Min( armor, hit.AcceptedDamage * fraction / ((1 - fraction) * ratio) );
		NpcTeamMemory.RecordArmorLoss( hit, lost );
		float damage = hit.AcceptedDamage + lost * ratio;
		return (armor - lost, damage > 0 ? AlienWounds.Flinch( hit, damage, maxHealth, end ) : end);
	}

	static (float Ratio, float Fraction) ArmorRule( DamageInfo hit, Component victim )
	{
		var tags = hit.Tags;
		if ( tags.Has( "armoronly" ) || (victim is not Mac && tags.Has( "nervegas" )) ) return (2, 1);
		if ( tags.Has( "falling" ) ) return (2, 0);
		if ( victim is not Drifter && tags.Has( "structuresonlylight" ) ) return (9, 0.9f);
		if ( tags.HasAny( "door", "structuralheavy", "heavy" ) ) return (1, 0.7f);
		return (tags.Has( "light" ) ? 4 : 2, 0.7f);
	}
}
