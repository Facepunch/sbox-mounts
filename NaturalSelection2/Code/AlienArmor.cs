namespace NS2;

internal static class AlienArmor
{
	internal static float HealthPerArmor( DamageInfo hit )
	{
		if ( hit.Tags.Has( "gas" ) ) return 0;
		if ( hit.Tags.Has( "heavy" ) ) return 1;
		return hit.Tags.Has( "light" ) ? 4 : 2;
	}
}
