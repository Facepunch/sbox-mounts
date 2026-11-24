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
	public static void Configure( PaydayWeapon weapon, WeaponDefinition definition )
	{
		weapon.WeaponId = definition.Id;
		weapon.DisplayName = definition.Title;
		weapon.HoldType = definition.Slot == 1 ? "pistol" : "rifle";
		weapon.PreferredSlot = definition.Slot;
		weapon.PrimaryAutomatic = definition.Automatic;
		weapon.PrimaryDelay = definition.Interval;
		weapon.DeployTime = 0.5f;
		weapon.ClipMaxSize = definition.Magazine;
		weapon.StartingAmmo = definition.Reserve;
		weapon.CanCancelReload = definition.Shotgun;
		weapon.IncrementalReloading = definition.Shotgun;
		weapon.ReloadStartTime = definition.Shotgun ? .4f + definition.Tactical : 0;
		weapon.ReloadTime = weapon.TacticalReloadTime = definition.Tactical;
		weapon.EmptyReloadTime = definition.Empty;
		weapon.Ballistics = new() { Damage = definition.Damage, Pellets = definition.Shotgun ? 6 : 1,
			Range = (definition.Shotgun ? (definition.Id == "reinbeck" ? 4000 : 2000) : 20000) / 2.54f, Radius = 0,
			Force = 300, SpreadBase = new Vector2( definition.Spread, definition.Spread ), SpreadGrowth = Vector2.Zero };
	}
}
