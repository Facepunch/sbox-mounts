using System;

namespace Source1;

/// <summary>
/// Counter-Strike: Source's code constants and the choices its weapon scripts leave to code, for the shared
/// mounted weapons. Garry's Mod's copy of the content mounts as the same game, without the scripts, so the
/// weapon family read from a model name stands in for the script's WeaponType.
/// </summary>
static class CssRules
{
	public const string Mount = "css";

	/// <summary>
	/// Approximation: Counter-Strike gives bots the player's damage, which ends an NPC firefight in a second or two.
	/// HL2's skill.cfg has NPC bullets hit for about 3 (sk_npc_dmg_smg1, _ar2, _pistol, _buckshot), a tenth of a
	/// Counter-Strike rifle round; at 0.15 the weapons keep their differences and a duel at 600 units lasts 3-12 seconds.
	/// </summary>
	public const float NpcDamageScale = 0.15f;

	/// <summary>CWeaponCSBaseGun toggles zoom, burst and silencer at most every 0.3s.</summary>
	public const float ToggleDelay = 0.3f;

	public const int BurstShots = 3;

	/// <summary>MuzzleFlashStyle values, used as flash types.</summary>
	public const string Flash = "CS_MUZZLEFLASH_NORM";
	public const string FlashX = "CS_MUZZLEFLASH_X";
	public const string FlashNone = "CS_MUZZLEFLASH_NONE";

	/// <summary>Scoped rifles (CWeaponAug, CWeaponSG552) zoom once.</summary>
	static readonly float[] ScopedRifleZoom = [55];
	/// <summary>CWeaponScout, CWeaponSG550 and CWeaponG3SG1 zoom twice; the AWP's second step (10) is in its catalog entry.</summary>
	static readonly float[] SniperRifleZoom = [40, 15];

	/// <summary>True for a weapon made from Counter-Strike: Source content.</summary>
	public static bool Owns( BaseCombatWeapon weapon ) => SourceMount.OfWeapon( weapon ) == Mount;

	/// <summary>
	/// Seconds between a burst's rounds and from its first round to the next trigger pull: CWeaponGlock18's
	/// kGlockBurstCycleTime 0.06 and 0.5, CWeaponFamas's kFamasBurstCycleTime 0.075 and 0.55. They are the only
	/// burst weapons, a pistol and a rifle.
	/// </summary>
	public static (float Interval, float Cycle) Burst( string weaponType ) => weaponType == "Pistol" ? (0.06f, 0.5f) : (0.075f, 0.55f);

	public static float[] ZoomLevels( string weaponType ) => weaponType == "SniperRifle" ? SniperRifleZoom : ScopedRifleZoom;

	public static bool IsFlash( string type ) => type is Flash or FlashX;

	/// <summary>
	/// A weapon family's usual MuzzleFlashStyle and MuzzleFlashScale, for Garry's Mod's copy of the content,
	/// which has no scripts: the X flash for rifles, machine guns and automatic snipers, the plain one otherwise.
	/// </summary>
	public static (string Type, float Scale)? FlashFor( string weaponType, bool automatic ) => weaponType switch
	{
		"Pistol" => (Flash, 1),
		"SubMachinegun" => (Flash, 1.1f),
		"Rifle" => (FlashX, 1.5f),
		"SniperRifle" => automatic ? (FlashX, 1.5f) : (Flash, 1.35f),
		"Shotgun" => (Flash, 1.3f),
		"Machinegun" => (FlashX, 1.5f),
		_ => null
	};

	/// <summary>CWeaponCSBase::PlayEmptySound: the scripts name no empty sound, the code clicks by weapon family.</summary>
	public static string EmptySound( string weaponType ) => weaponType == "Pistol" ? "Default.ClipEmpty_Pistol" : "Default.ClipEmpty_Rifle";

	/// <summary>The weapon family a world model's name carries: w_rif_ak47 is a Rifle.</summary>
	public static string WeaponTypeOf( string model )
	{
		var parts = System.IO.Path.GetFileNameWithoutExtension( model ?? "" ).Split( '_' );
		if ( parts.Length < 3 ) return null;
		return parts[1].ToLowerInvariant() switch
		{
			"pist" => "Pistol",
			"smg" => "SubMachinegun",
			"rif" => "Rifle",
			"snip" => "SniperRifle",
			"shot" => "Shotgun",
			"mach" => "Machinegun",
			"knife" => "Knife",
			"eq" => "Grenade",
			_ => null
		};
	}

	/// <summary>How a Counter-Strike grenade leaves the hand, see <see cref="CssGrenade.ThrowVelocity"/>.</summary>
	public static Vector3 ThrowVelocity( Vector3 forward, Vector3 inherited ) => CssGrenade.ThrowVelocity( forward, inherited );

	/// <summary>The thrown grenades, or null for a projectile kind that isn't Counter-Strike's.</summary>
	public static SourceProjectile Launch( SourceProjectile.Kinds kind, GameObject attacker, string mount, Vector3 origin, Vector3 velocity, float damage, float radius, string model, BaseCombatWeapon weapon ) => kind switch
	{
		SourceProjectile.Kinds.HeGrenade => SourceProjectile.Spawn<CssHeGrenade>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon ),
		SourceProjectile.Kinds.Flashbang => SourceProjectile.Spawn<CssFlashbang>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon ),
		SourceProjectile.Kinds.SmokeGrenade => SourceProjectile.Spawn<CssSmokeGrenade>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon ),
		_ => null
	};
}
