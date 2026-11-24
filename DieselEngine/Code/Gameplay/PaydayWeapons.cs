namespace Diesel;

/// <summary>Unupgraded first-weapon pilot. Art is always read from the installed bundles.</summary>
static class PaydayWeapons
{
	public const string ViewUnit = "units/weapons/m4_rifle/m4_rifle";
	public const string WorldUnit = "units/weapons/m4_rifle_npc/m4_rifle_npc";
	public const string Root = "entities/weapons/amcar4.prefab";
	public const string View = "entities/parts/amcar4_view.prefab";
	public const string World = "entities/parts/amcar4_world.prefab";
	public const string Ammo = "entities/ammo/amcar4.ammo";
	// PDTH WeaponTweakData.test_raycast_weapon/m4: 20 rounds, six magazines,
	// 0.1s fire interval, 1.4/1.9s reload. Damage uses a documented 10x sandbox scale.
	public static void Configure( PaydayWeapon weapon )
	{
		weapon.DisplayName = "AMCAR-4";
		weapon.HoldType = "rifle";
		weapon.PreferredSlot = 0;
		weapon.PrimaryAutomatic = true;
		weapon.PrimaryDelay = 0.1f;
		weapon.DeployTime = 0.5f;
		weapon.ClipMaxSize = 20;
		weapon.StartingAmmo = 100;
		weapon.CanCancelReload = false;
		weapon.ReloadTime = 1.4f;
		weapon.Ballistics = new() { Damage = 20, Pellets = 1, Range = 20000 / 2.54f, Radius = 0,
			Force = 300, SpreadBase = new Vector2( 4, 4 ), SpreadGrowth = Vector2.Zero };
	}
}
