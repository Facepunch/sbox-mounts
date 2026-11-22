namespace GoldSrc.HalfLife;

internal static class WeaponDamage
{
	internal static bool Deathmatch => Networking.MaxPlayers > 1;

	internal static float ForTarget( GameObject attacker, GameObject target, float normal, float deathmatch ) =>
		Deathmatch && IsPlayer( attacker ) && IsPlayer( target ) ? deathmatch : normal;

	internal static bool IsPlayer( GameObject obj ) => obj?.GetComponentInParent<PlayerController>().IsValid() == true;
}
