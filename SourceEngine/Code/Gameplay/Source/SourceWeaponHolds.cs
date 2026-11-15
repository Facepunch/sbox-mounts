using System;

namespace Source1;

/// <summary>
/// The holds a Source animator tries for a weapon, its own first, then the closest a model without that set can use.
/// A gun falls back to the nearest other gun. Past the end of the list the weapon is held unarmed.
/// </summary>
static class SourceWeaponHolds
{
	static readonly string[] Rifle = [WeaponHolds.Rifle, WeaponHolds.Smg, WeaponHolds.Shotgun];
	static readonly string[] Pistol = [WeaponHolds.Pistol, WeaponHolds.Smg, WeaponHolds.Rifle];

	/// <summary>The fallback chain for a well-known hold name, starting with itself. Empty for none or a name that isn't well-known.</summary>
	public static string[] Fallbacks( string hold ) => WeaponHolds.Normalize( hold ) switch
	{
		WeaponHolds.Smg => [WeaponHolds.Smg, WeaponHolds.Rifle, WeaponHolds.Pistol],
		WeaponHolds.Rifle => Rifle,
		WeaponHolds.Shoulder => [WeaponHolds.Shoulder, .. Rifle],
		WeaponHolds.Tool => [WeaponHolds.Tool, .. Rifle],
		WeaponHolds.Pistol => Pistol,
		WeaponHolds.PistolTwoHanded => [WeaponHolds.PistolTwoHanded, .. Pistol],
		WeaponHolds.DualPistols => [WeaponHolds.DualPistols, .. Pistol],
		WeaponHolds.Shotgun => [WeaponHolds.Shotgun, WeaponHolds.Rifle, WeaponHolds.Smg],
		WeaponHolds.Throwable => [WeaponHolds.Throwable, WeaponHolds.Pistol],
		WeaponHolds.Item => [WeaponHolds.Item, WeaponHolds.Pistol],
		WeaponHolds.MeleeTwoHanded => [WeaponHolds.MeleeTwoHanded, WeaponHolds.Melee, WeaponHolds.Fists],
		WeaponHolds.Knife => [WeaponHolds.Knife, WeaponHolds.Melee, WeaponHolds.Fists],
		WeaponHolds.Melee => [WeaponHolds.Melee, WeaponHolds.Fists],
		WeaponHolds.Fists => [WeaponHolds.Fists, WeaponHolds.Melee],
		_ => []
	};

	/// <summary>
	/// The well-known holds to try for a weapon, in order: its <see cref="BaseCombatWeapon.HoldType"/>, its
	/// <see cref="BaseCombatWeapon.HoldFallback"/>, then the rest of their fallback chains. Empty when neither is well-known.
	/// </summary>
	public static IEnumerable<string> For( BaseCombatWeapon weapon )
	{
		var hold = Fallbacks( weapon?.HoldType );
		var fallback = Fallbacks( weapon?.HoldFallback );
		return hold.Take( 1 ).Concat( fallback.Take( 1 ) ).Concat( hold.Skip( 1 ) ).Concat( fallback.Skip( 1 ) );
	}
}
