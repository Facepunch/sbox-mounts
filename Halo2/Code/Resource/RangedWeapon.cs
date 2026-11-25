using Halo2.Blam;
using Sandbox.Mounting;
using System;

namespace Halo2;

internal sealed class RangedWeaponLoader( MapFile source, TagEntry tag, string title, string hold ) : WeaponLoader( source, tag )
{
	internal string Title => title;

	internal static RangedWeaponLoader ForTag( MapFile map, TagEntry weapon, string name ) => name switch
	{
		"magnum" => new( map, weapon, "Magnum", WeaponHolds.Pistol ),
		"smg" => new( map, weapon, "SMG", WeaponHolds.Smg ),
		"battle_rifle" => new( map, weapon, "Battle Rifle", WeaponHolds.Rifle ),
		"sniper_rifle" => new( map, weapon, "Sniper Rifle", WeaponHolds.Rifle ),
		"covenant_carbine" => new( map, weapon, "Carbine", WeaponHolds.Rifle ),
		"plasma_rifle" => new( map, weapon, "Plasma Rifle", WeaponHolds.Rifle ),
		"brute_plasma_rifle" => new( map, weapon, "Brute Plasma Rifle", WeaponHolds.Rifle ),
		"beam_rifle" => new( map, weapon, "Beam Rifle", WeaponHolds.Rifle ),
		_ => null
	};

	string Name => System.IO.Path.GetFileName( weapon.TagPath.Replace( '\\', '/' ) );
	int PrimaryTrigger => Block( Header + 0x2c8 );
	int PrimaryBarrel => Block( Header + 0x2d0 ) + Short( PrimaryTrigger + 8 ) * 0xec;
	internal bool UsesMagazine => Short( PrimaryBarrel + 0x2a ) > 0;
	internal string AmmoPath => $"ammo/{Name}.ammo";
	PrefabFile prefab;

	protected override object Load()
	{
		var world = WorldModelPath;
		int barrel = PrimaryBarrel;
		int projectile = Address( Reference( barrel + 0x8c ) );
		int damage = Address( Reference( projectile + 0x148 ) );
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", title ).WithMetadata( "PreviewModel", world );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, title );
			RangedWeapon gun = UsesMagazine ? CreateMagazine( root, world, barrel ) : CreateBattery( root, world, barrel, projectile, damage );
			gun.HoldType = hold;
			gun.RigDirectory = $"mount://{Host.Ident}/weapons/{Name}";
			int models = Block( Header + 0x2a8 );
			gun.SpartanModel = Host.AssetPath( map, Reference( models ).TagPath, "vmdl" );
			if ( map.ReadInt32( Header + 0x2a8 ) > 1 && Reference( models + 16 ) is { PrimaryClass: "edom" } elite )
				gun.EliteModel = Host.AssetPath( map, elite.TagPath, "vmdl" );
			gun.ViewOffset = new Vector3( Float( Header + 0x308 ), Float( Header + 0x30c ), Float( Header + 0x310 ) ) * 120;
			gun.DeployTime = Float( Header + 0x13c );
			gun.PrimaryAutomatic = Short( PrimaryTrigger + 6 ) == 0;
			float rate = Float( barrel + 4 );
			float recovery = RecoveryTime( barrel );
			gun.PrimaryDelay = rate > 0 ? Weapon.CooldownSeconds( (int)MathF.Round( 30 / rate ) ) : recovery;
			gun.AttackSound = FiringSound( barrel );
			ConfigureKick( gun, barrel );
			var ballistics = gun.Ballistics;
			ballistics.Pellets = Short( barrel + 0x6a );
			ballistics.Range = Float( projectile + 0xe0 ) * 120;
			ballistics.Damage = Float( damage + 0x20 );
			ballistics.Force = Float( damage + 0x40 ) * 120;
			ballistics.SpreadBase = Vector2.One * Float( barrel + 0x74 ).RadianToDegree();
			ballistics.SpreadGrowth = Vector2.One * (Float( barrel + 0x78 ) - Float( barrel + 0x74 )).RadianToDegree();
			ballistics.Radius = 0;
			gun.Ballistics = ballistics;
		}
		return prefab = builder.Create();
	}

	float RecoveryTime( int barrel ) => Weapon.CooldownSeconds( (int)MathF.Floor( Float( barrel + 0x20 ) * 30 ) + 2 );

	MagazineWeapon CreateMagazine( GameObject root, string world, int barrel )
	{
		var gun = ConfigurePickup<MagazineWeapon>( root, world, title );
		int magazine = Block( Header + 0x2c0 ) + Short( barrel + 0x28 ) * 0x5c;
		gun.PrimaryAmmoType = GameResource.Load<BaseAmmoResource>( $"mount://{Host.Ident}/{AmmoPath}" );
		gun.ClipMaxSize = Short( magazine + 0xa );
		gun.StartingAmmo = Math.Max( 0, Short( magazine + 6 ) - gun.ClipMaxSize );
		gun.BurstLength = Short( barrel + 0x1c );
		gun.RecoveryDelay = RecoveryTime( barrel );
		var rig = (Halo2ModelLoader)Host.GetByPath( $"mount://{Host.Ident}/weapons/{Name}/first_person.vmdl" );
		var actions = rig.GetAnimationContext( "any", "any", "any" ).Actions;
		gun.ReloadTime = rig.GetClipInfo( actions["reload_full"] ).Frames / 30f;
		return gun;
	}

	BatteryWeapon CreateBattery( GameObject root, string world, int barrel, int projectile, int damage )
	{
		var gun = ConfigurePickup<BatteryWeapon>( root, world, title );
		gun.UsesAmmo = gun.UsesClips = false;
		gun.HeatPerShot = Float( barrel + 0xa4 );
		gun.SpentPerShot = Float( barrel + 0xa8 );
		gun.CooledThreshold = Float( Header + 0x150 );
		gun.OverheatThreshold = Float( Header + 0x154 );
		gun.HeatLoss = Float( Header + 0x160 );
		gun.OverheatedLoss = Float( Header + 0x168 );
		gun.Instantaneous = (map.ReadInt32( projectile + 0xbc ) & 32) != 0;
		gun.MisfireThreshold = Float( Header + 0x25c );
		gun.MisfireChance = Float( Header + 0x260 );
		gun.FiringRate = new Vector2( Float( barrel + 4 ), Float( barrel + 8 ) );
		gun.RateRise = Float( barrel + 0xd4 );
		gun.RateFall = Float( barrel + 0xd8 );
		gun.PelletSpeed = Float( projectile + 0x17c ) * 120;
		gun.MinimumPelletAngle = Float( barrel + 0x70 ).RadianToDegree();
		gun.AirGravityScale = Float( projectile + 0x164 );
		gun.WaterGravityScale = Float( projectile + 0x170 );
		bool falloff = (map.ReadInt32( projectile + 0xbc ) & 16) != 0;
		gun.DamageFalloffRange = falloff ? Float( projectile + 0x16c ) * 120 : 0;
		gun.UnderwaterDamageFalloffRange = falloff ? Float( projectile + 0x178 ) * 120 : 0;
		gun.MinimumDamage = Float( damage + 0x1c );
		gun.PelletBodyScales = BodyDamageScales( Address( map.Tags.First( t => t.PrimaryClass == "gtam" ) ), damage );
		gun.PelletExplosive = (map.ReadInt32( damage + 0x14 ) & 0x20) != 0;
		return gun;
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		base.Shutdown();
	}
}
