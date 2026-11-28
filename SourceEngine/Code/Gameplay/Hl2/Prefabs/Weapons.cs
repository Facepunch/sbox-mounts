using System;

namespace Source1.Hl2.Prefabs;

[Library]
public static class Weapons
{
	internal const float BulletImpulse = 667.973f;

	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> Weapon357() =>
		Define<BulletWeapon>( "357", ".357 Magnum", "357",
			w =>
			{
				w.DeployTime = 0.25f;
				w.PreferredSlot = 2;
				w.Ballistics = w.Ballistics with { Damage = 8, Range = 56755.84f, Radius = 0, Force = 10905.682f, SpreadBase = new( 0, 0 ), SpreadGrowth = new( 0, 0 ) };
				w.ClipMaxSize = 6;
				w.EmptyClickInterval = 0.5f;
				w.FiresUnderwater = false;
				w.PunchResponse = Weapon.PunchResponses.AddImpulse;
				w.PunchPitch = new( -8, -8 );
				w.PunchYaw = new( -2, 2 );
				w.PunchRoll = new( 0, 0 );
				w.RecoilPitch = new( -1, 1 );
				w.RecoilYaw = new( -1, 1 );
				w.TracerFrequency = 4;
				w.HoldType = "pistol_two_handed";
				w.Npc = w.Npc with { MinRange = 65, MaxRange = 1024 };
				w.PrimaryDelay = 0.75f;
				w.ReloadTime = 1.5f;
				w.StartingAmmo = 36;
			}, v =>
			{
				v.Fire = "fire";
			}, bulletEffects: true );

	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> Ar2() =>
		Define<BulletWeapon>( "ar2", "Pulse Rifle", "irifle",
			w =>
			{
				w.DeployTime = 0.208333f;
				w.PreferredSlot = 2;
				w.Ballistics = w.Ballistics with { Damage = 8, Range = 56755.84f, Radius = 0, Force = BulletImpulse, SpreadBase = new( 3, 3 ), SpreadGrowth = new( 0, 0 ) };
				w.ClipMaxSize = 45;
				w.ContinuousFireRate = true;
				w.EmptyClickInterval = 0.5f;
				w.SecondaryAutomatic = true;
				w.FiresUnderwater = false;
				w.MachineGunKick = new( 8, 5 );
				w.HoldType = "rifle";
				w.Npc = w.Npc with { MinRange = 65, MaxRange = 2048 };
				w.ReloadTime = 1.5f;
				w.SecondaryPickupAmmo = 3;
				w.StartingAmmo = 90;
			}, v =>
			{
				v.ResetRecoilOnRelease = true;
				v.Idle = "IR_idle";
				v.Draw = "IR_draw";
				v.Fire = "IR_fire";
				v.Reload = "IR_reload";
			}, bulletEffects: true );

	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> Bugbait() =>
		Define<ProjectileWeapon>( "bugbait", "Bugbait", "bugbait",
			w =>
			{
				w.Ballistics = w.Ballistics with { Damage = 8, Force = 300 };
				w.ClipMaxSize = 1;
				w.DeployTime = 0.291667f;
				w.Handedness = BaseCombatWeapon.WeaponHandedness.Right;
				w.RecoilPitch = new( 0, 0 );
				w.RecoilYaw = new( 0, 0 );
				w.PunchRoll = new( 0, 0 );
				w.HoldType = "grenade";
				w.HoldFallback = "item";
				w.PreferredSlot = 4;
				w.PrimaryDelay = 0.8f;
				w.ReloadTime = 1.7f;
				w.SecondaryAutomatic = true;
				w.SecondaryDelay = 0.6f;
				w.StartingAmmo = 12;
				w.UsableByNpcs = false;
				w.UsesAmmo = false;
				w.Kind = Projectile.Kinds.Bugbait;
				w.ProjectileModel = "models/weapons/w_bugbait.mdl";
				w.ProjectileDamage = 0;
			}, v =>
			{
				v.Fire = "throw";
				v.Reload = "draw";
			} );

	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> Crossbow() =>
		Define<ProjectileWeapon>( "crossbow", "Crossbow", "crossbow",
			w =>
			{
				w.Ballistics = w.Ballistics with { Damage = 8, Force = 300 };
				w.ClipMaxSize = 1;
				w.DeployTime = 0.25f;
				w.EmptyClickInterval = 0.5f;
				w.HoldType = "shotgun";
				w.Npc = w.Npc with { MinRange = 65, MaxRange = 1024 };
				w.PreferredSlot = 4;
				w.PrimaryDelay = 0.75f;
				w.AutoReloadDelay = 0.1f;
				w.PunchResponse = Weapon.PunchResponses.AddImpulse;
				w.PunchPitch = new( -2, -2 );
				w.PunchRoll = new( 0, 0 );
				w.RecoilPitch = new( 0, 0 );
				w.RecoilYaw = new( 0, 0 );
				w.ReloadTime = 1.7f;
				w.StartingAmmo = 12;
				w.ProjectileModel = "models/crossbow_bolt.mdl";
				w.Secondary = Weapon.SecondaryFires.Zoom;
			}, v =>
			{
				v.Idle = "idle";
				v.Fire = "fire";
			} );

	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> Crowbar() =>
		Define<MeleeWeapon>( "crowbar", "Crowbar", "crowbar",
			w =>
			{
				w.Ballistics = w.Ballistics with { Damage = 8, Force = 300 };
				w.ClipMaxSize = 1;
				w.DeployTime = 0.233333f;
				w.Handedness = BaseCombatWeapon.WeaponHandedness.Right;
				w.HoldType = "melee";
				w.PreferredSlot = 0;
				w.PrimaryDelay = 0.4f;
				w.ReloadTime = 1.7f;
				w.StartingAmmo = 12;
				w.UsableByNpcs = false;
				w.Damage = 10;
				w.Range = 75;
				w.SwingDelay = 0.4f;
				w.MissSwingDelay = 0.4f;
			}, v =>
			{
				v.Fire = "misscenter1";
				v.Reload = null;
			} );

	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> GravityGun() =>
		Define<GravityWeapon>( "gravity_gun", "Gravity Gun", "physcannon",
			w =>
			{
				w.Ballistics = w.Ballistics with { Damage = 8, Force = 300 };
				w.ClipMaxSize = 1;
				w.DeployTime = 0;
				w.HoldType = "tool";
				w.PreferredSlot = 4;
				w.PrimaryDelay = 0.5f;
				w.ReloadTime = 1.7f;
				w.StartingAmmo = 12;
				w.UsableByNpcs = false;
				w.UsesAmmo = false;
			}, v =>
			{
				v.Idle = "idle";
				v.Fire = "fire";
				v.Reload = null;
			}, worldModel: "physics" );

	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> Grenade() =>
		Define<ProjectileWeapon>( "grenade", "Frag Grenade", "grenade",
			w =>
			{
				w.Ballistics = w.Ballistics with { Damage = 8, Force = 300 };
				w.ClipMaxSize = 1;
				w.DeployTime = 0.316667f;
				w.EmptyClickInterval = 0.5f;
				w.Handedness = BaseCombatWeapon.WeaponHandedness.Right;
				w.RecoilPitch = new( 0, 0 );
				w.RecoilYaw = new( 0, 0 );
				w.PunchRoll = new( 0, 0 );
				w.HoldType = "grenade";
				w.HoldFallback = "item";
				w.PreferredSlot = 4;
				w.PrimaryDelay = 0.8f;
				w.ReloadTime = 0.5f;
				w.SecondaryAutomatic = true;
				w.StartingAmmo = 5;
				w.UsableByNpcs = false;
				w.Kind = Projectile.Kinds.Grenade;
				w.ProjectileModel = "models/weapons/w_npcnade.mdl";
				w.ProjectileDamage = 150;
			}, v =>
			{
				v.Fire = "throw";
				v.Reload = "draw";
			} );

	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> Pistol() =>
		Define<BulletWeapon>( "pistol", "Pistol", "pistol",
			w =>
			{
				w.PreferredSlot = 2;
				w.Ballistics = w.Ballistics with { Damage = 8, Range = 56755.84f, Radius = 0, Force = BulletImpulse, SpreadBase = new( 1, 1 ), SpreadGrowth = new( 5, 5 ), SpreadRecovery = 1.5f };
				w.ClipMaxSize = 18;
				w.PunchResponse = Weapon.PunchResponses.ResetImpulse;
				w.PunchPitch = new( 0.25f, 0.5f );
				w.PunchYaw = new( -0.6f, 0.6f );
				w.PunchRoll = new( 0, 0 );
				w.RecoilPitch = new( 0, 0 );
				w.RecoilYaw = new( 0, 0 );
				w.Handedness = BaseCombatWeapon.WeaponHandedness.Right;
				w.HoldType = "pistol";
				w.Npc = w.Npc with { MinRange = 24, MaxRange = 1500, BurstMin = 1, BurstMax = 3 };
				w.PrimaryDelay = 0.5f;
				w.Secondary = Weapon.SecondaryFires.DelayPrimary;
				w.TapRefireDelay = 0.1f;
				w.DryTapRefireDelay = 0.2f;
				w.AccuracyPenaltyPerShot = 0.2f;
				w.ReloadTime = 1.5f;
				w.StartingAmmo = 150;
			}, v =>
			{
				v.Fire = "fire";
				v.DryFire = "fireempty";
				v.RecoilResetTime = 0.5f;
			}, bulletEffects: true );

	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> Rpg() =>
		Define<ProjectileWeapon>( "rpg", "RPG", "rpg",
			w =>
			{
				w.Ballistics = w.Ballistics with { Damage = 8, Force = 300 };
				w.ClipMaxSize = 1;
				w.DeployTime = 0.275f;
				w.EmptyClickInterval = 0.5f;
				w.RecoilPitch = new( 0, 0 );
				w.RecoilYaw = new( 0, 0 );
				w.PunchRoll = new( 0, 0 );
				w.HoldType = "shoulder";
				w.Npc = w.Npc with { MinRange = 480, MaxRange = 6000 };
				w.PreferredSlot = 4;
				w.PrimaryDelay = 0.5f;
				w.ReloadTime = 2.2f;
				w.StartingAmmo = 3;
				w.Kind = Projectile.Kinds.Rocket;
				w.ProjectileModel = "models/weapons/w_missile.mdl";
				w.ProjectileDamage = 150;
			}, v =>
			{
				v.Idle = "idle1";
				v.Fire = "fire";
			}, worldModel: "rocket_launcher" );

	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> Shotgun() =>
		Define<BulletWeapon>( "shotgun", "Shotgun", "shotgun",
			w =>
			{
				w.DeployTime = 0.183333f;
				w.PreferredSlot = 2;
				w.AttackDamageTags = new TagSet( ["bullet", "buckshot"] );
				w.PelletCountSetting = "sk_plr_num_shotgun_pellets";
				w.Ballistics = w.Ballistics with { Pellets = 7, Damage = 8, Range = 56755.84f, Force = 1308.6818f, SpreadBase = new( 10, 10 ), SpreadGrowth = new( 0, 0 ) };
				w.ClipMaxSize = 45;
				w.PunchResponse = Weapon.PunchResponses.AddImpulse;
				w.PunchPitch = new( -2, -1 );
				w.PunchYaw = new( -2, 2 );
				w.PunchRoll = new( 0, 0 );
				w.RecoilPitch = new( 0, 0 );
				w.RecoilYaw = new( 0, 0 );
				w.FiresUnderwater = false;
				w.TracerFrequency = 4;
				w.HoldType = "shotgun";
				w.Npc = w.Npc with { MaxRange = 500, BurstMin = 1, BurstMax = 1, RestMin = 0.75f, RestMax = 1.2f };
				w.PrimaryDelay = 0.75f;
				w.ReloadTime = 1.5f;
				w.StartingAmmo = 30;
				w.Secondary = Weapon.SecondaryFires.DoubleShot;
				w.SecondaryAutomatic = true;
			}, v =>
			{
				v.Reload = "reload1";
				v.ReloadInsert = "reload2";
				v.ReloadEnd = "reload3";
				v.Pump = "pump";
			}, bulletEffects: true );

	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> Smg() =>
		Define<BulletWeapon>( "smg", "SMG", "smg1",
			w =>
			{
				w.DeployTime = 0.208333f;
				w.PreferredSlot = 2;
				w.Ballistics = w.Ballistics with { Damage = 8, Range = 56755.84f, Radius = 0, Force = BulletImpulse, SpreadBase = new( 5, 5 ), SpreadGrowth = new( 0, 0 ) };
				w.ClipMaxSize = 45;
				w.EmptyClickInterval = 0.5f;
				w.SecondaryAutomatic = true;
				w.FiresUnderwater = false;
				w.CanLaunchDuringReload = true;
				w.PrimaryDelayAfterLaunch = 0.5f;
				w.MachineGunKick = new( 1, 2 );
				w.HoldType = "smg";
				w.ContinuousFireRate = true;
				w.PrimaryDelay = 0.075f;
				w.ReloadTime = 1.5f;
				w.SecondaryPickupAmmo = 3;
				w.StartingAmmo = 135;
			}, v =>
			{
				v.RecoilStages = [1, 3];
				v.ResetRecoilOnRelease = true;
			}, bulletEffects: true );

	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> Stunstick() =>
		Define<Stunstick>( "stunstick", "Stunstick", "stunstick",
			w =>
			{
				w.Ballistics = w.Ballistics with { Damage = 8, Force = 300 };
				w.ClipMaxSize = 1;
				w.DeployTime = 0.233333f;
				w.Handedness = BaseCombatWeapon.WeaponHandedness.Right;
				w.HoldType = "melee";
				w.PreferredSlot = 0;
				w.PrimaryDelay = 0.8f;
				w.ReloadTime = 1.7f;
				w.StartingAmmo = 12;
				w.UsableByNpcs = false;
				w.Damage = 10;
				w.Range = 75;
				w.SwingDelay = 0.8f;
				w.MissSwingDelay = 0.8f;
			}, v =>
			{
				v.Fire = "misscenter1";
				v.Reload = null;
			}, worldModel: "stunbaton" );

	static IEnumerable<EntityDefinition> Define<T>( string name, string title, string model, Action<T> configure,
		Action<ViewModel> configureView, bool bulletEffects = false, string worldModel = null ) where T : Weapon, new() =>
		EntityPrefab.WeaponSet<T>( "hl2", name, title, $"models/weapons/w_{worldModel ?? model}.mdl", $"models/weapons/c_{model}.mdl", w =>
		{
			w.DisplayName = $"HL2 {title}";
			w.Value = 10;
			w.CanCancelReload = false;
			w.SecondaryDelay = 1;
			configure( w );
		}, v =>
		{
			v.DrawPlaybackRate = 4;
			if ( bulletEffects )
			{
				v.MuzzleEffect = GameObject.GetPrefab( "prefabs/effects/default_muzzleflash.prefab" );
				v.TracerEffect = GameObject.GetPrefab( "prefabs/effects/default_tracer.prefab" );
			}
			else v.EjectBrass = null;
			configureView( v );
		}, fallback: $"models/weapons/v_{model}.mdl", worldEffects: bulletEffects );
}
