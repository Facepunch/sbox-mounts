namespace NS2;

internal static class AlienArmor
{
	internal static void Absorb( Component.IDamageable.DamageEvent hit, float armor )
	{
		if ( hit.AcceptedDamage <= 0 ) return;
		if ( hit.DamageInfo.Tags.HasAny( "bullet", "buckshot", "railgun", "grenade" ) && hit.Victim.GetComponent<UmbraProtection>().IsValid() )
			hit.AcceptedDamage *= 0.8f;
		hit.AcceptedDamage -= System.Math.Min( hit.AcceptedDamage * 0.7f, armor * HealthPerArmor( hit.DamageInfo ) );
	}

	internal static float HealthPerArmor( DamageInfo hit )
	{
		if ( hit.Tags.Has( "gas" ) ) return 0;
		if ( hit.Tags.Has( "heavy" ) ) return 1;
		return hit.Tags.Has( "light" ) ? 4 : 2;
	}
}
