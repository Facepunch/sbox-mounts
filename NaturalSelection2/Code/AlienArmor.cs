namespace NS2;

internal static class AlienArmor
{
	internal static float UmbraScale( Component.IDamageable.DamageEvent hit ) =>
		hit.DamageInfo.Tags.HasAny( "bullet", "buckshot", "railgun", "grenade" ) && hit.Victim.GetComponent<UmbraProtection>().IsValid() ? 0.8f : 1;

	internal static float ArmorOnlyScale( DamageInfo info ) =>
		info.Tags.Has( "corrode" ) ? 0.12f : info.Tags.HasAny( "armoronly", "nervegas" ) ? 1 : 0;

	internal static float ArmorOnlyDamage( Component.IDamageable.DamageEvent hit ) =>
		hit.DamageInfo.Damage * ArmorOnlyScale( hit.DamageInfo ) * UmbraScale( hit );

	internal static void ApplyPlayerRules( Component.IDamageable.DamageEvent hit, Component victim, float armor )
	{
		var tags = hit.DamageInfo.Tags;
		if ( hit.AcceptedDamage <= 0 || tags.HasAny( "splash", "structuresonly" ) || (victim is Marine && tags.Has( "nervegas" )) )
		{
			hit.Cancelled = true;
			return;
		}
		if ( tags.HasAny( "corrode", "nervegas", "armoronly" ) )
		{
			hit.AcceptedDamage = 0;
			hit.Cancelled = armor <= 0;
			return;
		}
		hit.AcceptedDamage *= (victim is Skulk or Lerk) && tags.Has( "spreading" ) ? 0.75f : PlayerDamageScale( hit.DamageInfo );
	}

	internal static void Absorb( Component.IDamageable.DamageEvent hit, float armor )
	{
		if ( hit.AcceptedDamage <= 0 ) return;
		hit.AcceptedDamage -= System.Math.Min( hit.AcceptedDamage * 0.7f, armor * HealthPerArmor( hit.DamageInfo ) );
	}

	internal static float PlayerDamageScale( DamageInfo hit )
	{
		if ( hit.Tags.Has( "puncture" ) ) return 2;
		if ( hit.Tags.Has( "machinegun" ) ) return 1.5f;
		return hit.Tags.HasAny( "clusterflame", "clusterflamefragment" ) ? 0.2f : 1;
	}

	internal static float HealthPerArmor( DamageInfo hit )
	{
		if ( hit.Tags.HasAny( "gas", "falling" ) ) return 0;
		if ( hit.Tags.HasAny( "heavy", "door", "structuralheavy" ) ) return 1;
		return hit.Tags.Has( "light" ) ? 4 : 2;
	}
}
