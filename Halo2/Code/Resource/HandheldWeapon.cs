using Halo2.Blam;
using Sandbox.Mounting;
using System;

namespace Halo2;

internal sealed class HandheldWeaponLoader( MapFile source, TagEntry tag, string title, string hold ) : WeaponLoader( source, tag )
{
	internal string Title => title;

	internal static HandheldWeaponLoader ForTag( MapFile map, TagEntry weapon, string name ) => name switch
	{
		"head" => new( map, weapon, "Oddball", WeaponHolds.Melee ),
		"head_sp" => new( map, weapon, "Campaign Skull", WeaponHolds.Melee ),
		"energy_blade" => new( map, weapon, "Energy Sword", WeaponHolds.Melee ),
		"energy_blade_useless" => new( map, weapon, "Depleted Energy Sword", WeaponHolds.Melee ),
		"plasma_pistol" => new( map, weapon, "Plasma Pistol", WeaponHolds.Pistol ),
		"magnum" => new( map, weapon, "Magnum", WeaponHolds.Pistol ),
		"rocket_launcher" => new( map, weapon, "Rocket Launcher", WeaponHolds.Shoulder ),
		"scarab_main_gun_handheld" => new( map, weapon, "Scarab Gun", WeaponHolds.Rifle ),
		"brute_shot" => new( map, weapon, "Brute Shot", WeaponHolds.Rifle ),
		"flak_cannon" => new( map, weapon, "Fuel Rod Gun", WeaponHolds.Shoulder ),
		"phantom_turret_handheld" => new( map, weapon, "Phantom Turret", WeaponHolds.Rifle ),
		"big_needler_handheld" => new( map, weapon, "Big Needler", WeaponHolds.Rifle ),
		"needler" => new( map, weapon, "Needler", WeaponHolds.Pistol ),
		"smg" => new( map, weapon, "SMG", WeaponHolds.Smg ),
		"battle_rifle" => new( map, weapon, "Battle Rifle", WeaponHolds.Rifle ),
		"sniper_rifle" => new( map, weapon, "Sniper Rifle", WeaponHolds.Rifle ),
		"covenant_carbine" => new( map, weapon, "Carbine", WeaponHolds.Rifle ),
		"plasma_rifle" => new( map, weapon, "Plasma Rifle", WeaponHolds.Rifle ),
		"brute_plasma_rifle" => new( map, weapon, "Brute Plasma Rifle", WeaponHolds.Rifle ),
		"sentinel_aggressor_beam" => new( map, weapon, "Sentinel Beam", WeaponHolds.Rifle ),
		"sent_agg_beam_elim" => new( map, weapon, "Sentinel Beam (Major)", WeaponHolds.Rifle ),
		"beam_rifle_noplayer" => new( map, weapon, "Beam Rifle (NPC)", WeaponHolds.Rifle ),
		"beam_rifle" => new( map, weapon, "Beam Rifle", WeaponHolds.Rifle ),
		_ => null
	};

	bool HasImpactBlast => Name is "flak_cannon" or "rocket_launcher" or "brute_shot" or "scarab_main_gun_handheld";

	TagEntry DetonationEffect
	{
		get
		{
			if ( !HasImpactBlast && Name is not ("big_needler_handheld" or "phantom_turret_handheld" or "needler") ) return null;
			int projectile = Address( Reference( PrimaryBarrel + 0x8c ) );
			return Reference( projectile + 0x140 ) ?? Reference( projectile + 0xf8 );
		}
	}

	internal void RegisterDetonation( MountContext context, HashSet<string> registered )
	{
		Register( context, DetonationEffect );
		if ( Name == "needler" ) Register( context, Reference( Address( Reference( PrimaryBarrel + 0x8c ) ) + 0x110 ) );

		void Register( MountContext mount, TagEntry tag )
		{
			if ( tag is { PrimaryClass: "effe" } && registered.Add( ImpactPath( tag ) ) )
				mount.Add( ResourceType.PrefabFile, ImpactPath( tag ), new ImpactLoader( this, tag, includeSounds: true ) { Flags = ResourceFlags.DeveloperOnly | ResourceFlags.Effect } );
		}
	}

	string Name => TagName( weapon );
	static string TagName( TagEntry entry ) => System.IO.Path.GetFileName( entry.TagPath.Replace( '\\', '/' ) );
	int PrimaryTrigger => Block( Header + 0x2c8 );
	int PrimaryBarrel => Block( Header + 0x2d0 ) + Short( PrimaryTrigger + 8 ) * 0xec;
	bool HasBarrel => map.ReadInt32( Header + 0x2d0 ) > 0;
	internal bool UsesMagazine => HasBarrel && Short( PrimaryBarrel + 0x2a ) > 0;
	internal string AmmoPath => $"ammo/{Name}.ammo";
	PrefabFile prefab;

	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = ResourceType.Texture;
		if ( Name != "rocket_launcher" ) return null;
		bool split = name.StartsWith( "split_", StringComparison.Ordinal );
		if ( split ) name = name[6..];
		var widget = name switch
		{
			"lockable.vtex" => "lockable",
			"locked.vtex" => "locked",
			"locking.vtex" => "locking",
			_ => null
		};
		if ( widget is null || HudPage( widget, split ) is not {} page ) return null;
		return new Halo2TextureLoader( map, page.Bitmap, page.Index, hudMask: true );
	}

	protected override object Load()
	{
		var world = WorldModelPath;
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", title ).WithMetadata( "PreviewModel", world );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, title );
			var item = HasBarrel ? CreateFirearm( root, world ) : Name switch
			{
				"energy_blade" or "energy_blade_useless" => CreateSword( root, world ),
				_ => ConfigurePickup<MeleeWeapon>( root, world, title )
			};
			item.HoldType = hold;
			item.RigDirectory = $"mount://{Host.Ident}/weapons/{Name}";
			int models = Block( Header + 0x2a8 );
			if ( Reference( models ) is { PrimaryClass: "edom" } spartan )
				item.SpartanModel = Host.AssetPath( map, spartan.TagPath, "vmdl" );
			if ( map.ReadInt32( Header + 0x2a8 ) > 1 && Reference( models + 16 ) is { PrimaryClass: "edom" } elite )
				item.EliteModel = Host.AssetPath( map, elite.TagPath, "vmdl" );
			item.ViewOffset = new Vector3( Float( Header + 0x308 ), Float( Header + 0x30c ), Float( Header + 0x310 ) ) * 120;
			item.DeployTime = Float( Header + 0x13c );
		}
		return prefab = builder.Create();
	}

	EnergySword CreateSword( GameObject root, string world )
	{
		var sword = ConfigurePickup<EnergySword>( root, world, title );
		sword.TargetAngle = Float( Header + 0x208 );
		sword.TargetRange = Float( Header + 0x20c ) * 120;
		sword.AgesWithKills = (map.ReadInt32( Header + 0x12c ) & (1 << 26)) != 0;
		if ( Reference( Header + 0x300 ) is { PrimaryClass: "paew" } aged )
			sword.AgedWeaponPrefab = $"mount://{Host.Ident}/weapons/{TagName( aged )}.prefab";
		if ( Reference( Header + 0x2f8 ) is { PrimaryClass: "effe" } aging && map.ReadInt32( Address( aging ) + 12 ) == 1 )
		{
			sword.AgeEffectMarker = map.ResolveStringId( map.ReadInt32( Block( Address( aging ) + 12 ) ) );
			root.AddComponent<PelletImpactEffect>().Visuals = ReadContactVisuals( aging, includeSounds: true, atMarker: true );
		}
		if ( Reference( Header + 0x140 ) is not { PrimaryClass: "effe" } ready ) return sword;

		int effect = Address( ready );
		if ( map.ReadInt32( effect + 0x14 ) == 0 ) return sword;
		int firstEvent = Block( effect + 0x14 );
		int part = FirstSoundPart( firstEvent );
		if ( part < 0 ) return sword;
		int location = Short( part + 4 );
		if ( (uint)location >= map.ReadInt32( effect + 12 ) ) return sword;
		string path = Host.AssetPath( map, Reference( part + 12 ).TagPath, "sound" );
		if ( Host.GetByPath( path ) is Halo2SoundLoader loader )
		{
			sword.ReadySound = loader.Cue;
			sword.ReadySoundDelay = Float( firstEvent + 8 );
			sword.ReadySoundMarker = map.ResolveStringId( map.ReadInt32( Block( effect + 12 ) + location * 4 ) );
		}
		return sword;
	}

	HandheldWeapon CreateFirearm( GameObject root, string world )
	{
		int barrel = PrimaryBarrel;
		int projectile = Address( Reference( barrel + 0x8c ) );
		int damage = Address( Reference( projectile + 0x148 ) );
		HandheldWeapon gun = UsesMagazine ? CreateMagazine( root, world, barrel ) : CreateBattery( root, world, barrel );
		gun.PrimaryAutomatic = Short( PrimaryTrigger + 6 ) == 0;
		float rate = Float( barrel + 4 );
		float recovery = RecoveryTime( barrel );
		gun.PrimaryDelay = rate > 0 ? Weapon.CooldownSeconds( (int)MathF.Round( 30 / rate ) ) : recovery;
		gun.AttackSound = FiringSound( barrel, out var marker );
		gun.AttackSoundMarker = marker;
		if ( map.ReadInt32( barrel + 0xe4 ) == 1 )
			gun.PrimaryFlash = ReadMuzzleFlash( Reference( Block( barrel + 0xe4 ) + 4 ) );
		ConfigureKick( gun, barrel );
		gun.Ballistics = ReadBallistics( gun.Ballistics, barrel, projectile, damage );
		ConfigureProjectile( gun, barrel, projectile, damage );
		return gun;
	}

	BlastDamage ReadBlast( TagEntry damage )
	{
		int area = Address( damage );
		return new( Float( area ) * 120, Float( area + 4 ) * 120, Float( area + 0x18 ) * 120,
			Float( area + 0x1c ), Float( area + 0x20 ), Float( area + 0x40 ) * 120,
			BodyDamageScales( Address( map.Tags.First( t => t.PrimaryClass == "gtam" ) ), area ) );
	}

	void ConfigureProjectile( HandheldWeapon gun, int barrel, int projectile, int damage )
	{
		if ( Name == "needler" )
		{
			gun.Shard = new( new Vector2( Float( projectile + 0x184 ), Float( projectile + 0x188 ) ),
				Float( Header + 0x208 ), Float( Header + 0x20c ) * 120, new Vector2( Float( projectile + 0xd4 ), Float( projectile + 0xd8 ) ),
				ReadBlast( Reference( projectile + 0x108 ) ), Reference( barrel + 0x8c ).TagPath,
				Short( projectile + 0xe6 ), ReadBlast( Reference( projectile + 0x12c ) ), ReadBlast( Reference( projectile + 0x118 ) ),
				Host.AssetPath( map, Reference( projectile + 0x110 ).TagPath, "prefab" ) );
			gun.ProjectileModel = ModelPath( Host, map, Reference( barrel + 0x8c ) );
		}
		if ( HasImpactBlast && Reference( projectile + 0x100 ) is { PrimaryClass: "!tpj" } explosion )
		{
			if ( Short( projectile + 0xc0 ) == 3 && map.ReadInt32( projectile + 0x19c ) == 1 )
			{
				int response = Block( projectile + 0x19c );
				if ( Short( response + 2 ) == 0 && Short( response + 0x14 ) == 4 && Short( response + 0x16 ) == 2 && Float( response + 0x18 ) == 1 )
					gun.Bounce = new( Float( response + 0x1c ), Float( response + 0x20 ), Float( response + 0x50 ), Float( response + 0x54 ),
						(int)MathF.Round( Float( projectile + 0xd4 ) * 30 ) );
			}
			gun.ProjectileModel = ModelPath( Host, map, Reference( barrel + 0x8c ) );
			gun.ImpactBlast = ReadBlast( explosion );
		}
		if ( DetonationEffect is { PrimaryClass: "effe" } effect )
		{
			string path = Host.AssetPath( map, effect.TagPath, "prefab" );
			if ( gun.Shard is {} shard ) gun.Shard = shard with { DetonationEffect = path };
			else gun.DetonationEffectPath = path;
		}
		gun.Trail = ReadTrail( projectile );
		gun.Instantaneous = (map.ReadInt32( projectile + 0xbc ) & 32) != 0;
		gun.PelletSpeed = Float( projectile + 0x17c ) * 120;
		gun.SpeedProfile = ReadSpeedProfile( projectile );
		gun.MinimumPelletAngle = Float( barrel + 0x70 ).RadianToDegree();
		gun.AirGravityScale = Float( projectile + 0x164 );
		gun.WaterGravityScale = Float( projectile + 0x170 );
		bool falloff = (map.ReadInt32( projectile + 0xbc ) & 16) != 0;
		gun.DamageFalloffRange = falloff ? Float( projectile + 0x16c ) * 120 : 0;
		gun.UnderwaterDamageFalloffRange = falloff ? Float( projectile + 0x178 ) * 120 : 0;
		gun.MinimumDamage = Float( damage + 0x1c );
		gun.PelletBodyScales = BodyDamageScales( Address( map.Tags.First( t => t.PrimaryClass == "gtam" ) ), damage );
		gun.PelletExplosive = (map.ReadInt32( damage + 0x14 ) & 0x20) != 0;
	}

	BaseCombatWeapon.BallisticConfig ReadBallistics( BaseCombatWeapon.BallisticConfig ballistics, int barrel, int projectile, int damage )
	{
		ballistics.Pellets = Short( barrel + 0x6a );
		ballistics.Range = Float( projectile + 0xe0 ) * 120;
		ballistics.Damage = Float( damage + 0x20 );
		ballistics.Force = Float( damage + 0x40 ) * 120;
		ballistics.SpreadBase = Vector2.One * Float( barrel + 0x74 ).RadianToDegree();
		ballistics.SpreadGrowth = Vector2.One * (Float( barrel + 0x78 ) - Float( barrel + 0x74 )).RadianToDegree();
		ballistics.Radius = 0;
		return ballistics;
	}

	float RecoveryTime( int barrel ) => Weapon.CooldownSeconds( (int)MathF.Floor( Float( barrel + 0x20 ) * 30 ) + 2 );

	MagazineWeapon CreateMagazine( GameObject root, string world, int barrel )
	{
		MagazineWeapon gun;
		if ( Short( PrimaryTrigger + 6 ) == 5 )
		{
			var launcher = ConfigurePickup<RocketLauncher>( root, world, title );
			launcher.LockAngle = Float( Header + 0x208 );
			launcher.LockRange = Float( Header + 0x20c ) * 120;
			int secondary = Block( Header + 0x2d0 ) + Short( PrimaryTrigger + 10 ) * 0xec;
			launcher.GuidedTurnRate = Float( Address( Reference( secondary + 0x8c ) ) + 0x184 );
			gun = launcher;
		}
		else gun = ConfigurePickup<MagazineWeapon>( root, world, title );
		int magazine = Block( Header + 0x2c0 ) + Short( barrel + 0x28 ) * 0x5c;
		gun.PrimaryAmmoType = GameResource.Load<BaseAmmoResource>( $"mount://{Host.Ident}/{AmmoPath}" );
		gun.ClipMaxSize = Short( magazine + 0xa );
		gun.StartingAmmo = Math.Max( 0, Short( magazine + 6 ) - gun.ClipMaxSize );
		gun.BurstLength = Short( barrel + 0x1c );
		gun.DryFireSound = FiringSound( barrel, out var dryMarker, dry: true );
		gun.DryFireMarker = dryMarker;
		gun.RecoveryDelay = RecoveryTime( barrel );
		int barrels = Block( Header + 0x2d0 );
		for ( int i = 0; i < map.ReadInt32( Header + 0x2d0 ); i++ )
		{
			int other = barrels + i * 0xec;
			if ( other != barrel && Short( other + 0x28 ) == Short( barrel + 0x28 ) )
				gun.HasIdleMagazineBarrel = true;
		}
		var rig = (Halo2ModelLoader)Host.GetByPath( $"mount://{Host.Ident}/weapons/{Name}/first_person.vmdl" );
		var actions = rig.GetAnimationContext( "any", "any", "any" ).Actions;
		gun.ReloadTime = rig.GetClipInfo( actions["reload_full"] ).Frames / 30f;
		return gun;
	}

	BatteryWeapon CreateBattery( GameObject root, string world, int barrel )
	{
		BatteryWeapon gun;
		if ( Short( PrimaryTrigger + 6 ) == 2 && Short( PrimaryTrigger + 24 ) == 1 )
		{
			var pistol = ConfigurePickup<PlasmaPistol>( root, world, title );
			pistol.LatchTicks = (int)MathF.Round( Float( PrimaryTrigger + 16 ) * 30 );
			pistol.ChargeTicks = (int)MathF.Round( Float( PrimaryTrigger + 28 ) * 30 );
			pistol.ChargeSound = ReadChargeSound();
			int secondary = Block( Header + 0x2d0 ) + Short( PrimaryTrigger + 10 ) * 0xec;
			int bolt = Address( Reference( secondary + 0x8c ) );
			int impact = Address( Reference( bolt + 0x148 ) );
			bool fades = (map.ReadInt32( bolt + 0xbc ) & 16) != 0;
			pistol.ChargedShot = new( ReadBallistics( pistol.Ballistics, secondary, bolt, impact ), Float( bolt + 0x17c ) * 120,
				Float( secondary + 0x70 ).RadianToDegree(), Float( bolt + 0x164 ), Float( bolt + 0x170 ), Float( impact + 0x1c ),
				fades ? Float( bolt + 0x16c ) * 120 : 0, fades ? Float( bolt + 0x178 ) * 120 : 0,
				BodyDamageScales( Address( map.Tags.First( t => t.PrimaryClass == "gtam" ) ), impact ), (map.ReadInt32( impact + 20 ) & 32) != 0,
				ReadTrail( bolt ), Float( secondary + 0xa4 ), Float( secondary + 0xa8 ), FiringSound( secondary, out var chargedMarker ), chargedMarker,
				Float( bolt + 0x188 ), Float( Header + 0x208 ), Float( Header + 0x20c ) * 120 ) { SpeedProfile = ReadSpeedProfile( bolt ) };
			gun = pistol;
		}
		else gun = ConfigurePickup<BatteryWeapon>( root, world, title );
		gun.UsesAmmo = gun.UsesClips = false;
		gun.HeatPerShot = Float( barrel + 0xa4 );
		gun.SpentPerShot = Float( barrel + 0xa8 );
		gun.CooledThreshold = Float( Header + 0x150 );
		gun.OverheatThreshold = Float( Header + 0x154 );
		gun.HeatLoss = Float( Header + 0x160 );
		gun.OverheatedLoss = Float( Header + 0x168 );
		gun.MisfireThreshold = Float( Header + 0x25c );
		gun.MisfireChance = Float( Header + 0x260 );
		gun.FiringRate = new Vector2( Float( barrel + 4 ), Float( barrel + 8 ) );
		gun.RateRise = Float( barrel + 0xd4 );
		gun.RateFall = Float( barrel + 0xd8 );
		int count = map.ReadInt32( barrel + 0xe4 );
		if ( count > 1 && (map.ReadInt32( barrel ) & 2) == 0 )
		{
			var cues = new FiringCue[count];
			for ( int i = 0; i < count; i++ )
			{
				int row = Block( barrel + 0xe4 ) + i * 52;
				int repeats = Short( row );
				if ( repeats <= 0 || repeats != Short( row + 2 ) ) return gun;
				var sound = FiringSound( barrel, out var marker, i );
				cues[i] = new( sound, marker, repeats, ReadMuzzleFlash( Reference( row + 4 ) ) );
			}
			gun.FiringCues = cues;
		}
		return gun;
	}

	MuzzleFlashDefinition ReadMuzzleFlash( TagEntry effect )
	{
		if ( effect is not { PrimaryClass: "effe", DataSize: > 0 } ) return null;
		int header = Address( effect );
		if ( map.ReadInt32( header + 20 ) == 0 ) return null;
		string marker = null;
		var layers = new List<MuzzleFlashLayer>();
		for ( int i = 0, ev = Block( header + 20 ); i < map.ReadInt32( header + 20 ); i++, ev += 56 )
			ReadSystems( ev + 48 );
		return layers.Count == 0 ? null : new( marker, layers.ToArray() );

		void ReadSystems( int systems )
		{
			for ( int i = 0, row = Block( systems ); i < map.ReadInt32( systems ); i++, row += 56 )
			{
				var particle = Reference( row );
				if ( particle?.TagPath is not (@"effects\generic\energy\flash_c_generic" or @"effects\generic\energy\flash_c_generic_med" or @"effects\generic\energy\plasma_electric_burst"
					or @"effects\objects\weapons\rifle\smg\fire_burst_center" or @"effects\objects\weapons\rifle\smg\fire_burst_long") ) continue;
				int definition = Address( particle );
				var bitmap = map.GetTag( map.ReadInt32( Block( Block( definition + 140 ) + 4 ) ) & 0xffff );
				if ( bitmap is null ) continue;
				var frame = ReadAnimatedFrames( definition )[0];
				var pivot = frame.Pivot * frame.InverseSize;
				marker ??= map.ResolveStringId( map.ReadInt32( Block( header + 12 ) + 4 * map.ReadInt32( row + 8 ) ) );
				for ( int j = 0, emitter = Block( row + 48 ); j < map.ReadInt32( row + 48 ); j++, emitter += 0xb8 )
				{
					int color = Block( emitter + 96 );
					Color Tint( int offset )
					{
						int rgb = map.ReadInt32( color + offset );
						return Color.FromBytes( (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb );
					}
					var offset = new Vector3( Float( emitter + 156 ), Float( emitter + 160 ), Float( emitter + 164 ) ) * 120;
					var direction = Rotation.From( -Float( emitter + 172 ).RadianToDegree(), Float( emitter + 168 ).RadianToDegree(), 0 );
					Vector2 Bounds( int property ) => new( Float( Block( property + 8 ) + 4 ), Float( Block( property + 8 ) + 8 ) );
					layers.Add( new( Host.AssetPath( map, bitmap.TagPath, "vtex" ) + "/sheet.vtex", Short( definition + 8 ),
						new Vector2( 1 - pivot.x, pivot.y ), frame.Aspect, Bounds( emitter + 24 ),
						ReadRange( emitter + 72, 120 * Bounds( definition + 60 ).x ), ReadRange( emitter + 56, 1 ), ReadRange( definition + 92, 1 ),
						Tint( 4 ), (map.ReadInt32( color ) & 0xff00) == 0x2000 ? Tint( 16 ) : Tint( 4 ),
						offset, direction, Short( definition + 4 ) == 1, Short( row + 18 ), Short( row + 12 ) == 1,
						Bounds( emitter + 8 ), Bounds( emitter + 124 ) * 120, Bounds( emitter + 40 ).x * 120, Short( emitter + 72 ) == 2 ) );
				}
			}
		}
	}

	ChargeSoundTrack ReadChargeSound()
	{
		for ( int i = 0, row = Block( Header + 0x94 ); i < map.ReadInt32( Header + 0x94 ); i++, row += 24 )
		{
			if ( Reference( row ) is not { PrimaryClass: "dnsl", DataSize: > 0 } sound ||
				map.ResolveStringId( map.ReadInt32( row + 16 ) ) != "primary_charged" ) continue;
			int tracks = Address( sound ) + 0x24;
			if ( map.ReadInt32( tracks ) != 1 ) continue;
			int track = Block( tracks );
			SoundEvent Cue( int offset ) => Reference( track + offset ) is { PrimaryClass: "!dns" } tag
				? GameResource.Load<SoundEvent>( Host.AssetPath( map, tag.TagPath, "sound" ) ) : null;
			return new( map.ResolveStringId( map.ReadInt32( row + 8 ) ), Cue( 0x14 ), Cue( 0x1c ),
				MathF.Pow( 10, Float( track + 8 ) / 20 ), Float( track + 16 ) );
		}
		return null;
	}

	ContrailAppearance ReadTrail( int projectile )
	{
		int attachments = projectile + 0x94;
		for ( int i = 0, row = Block( attachments ); i < map.ReadInt32( attachments ); i++, row += 24 )
		{
			if ( Reference( row ) is not { PrimaryClass: "tnoc", DataSize: >= 0xf0 } tag ) continue;
			int trail = Address( tag );
			if ( Short( trail + 0x18 ) != 4 || Short( trail + 0x66 ) != 3 ||
				Reference( trail + 0x30 ) is not { PrimaryClass: "mtib" } texture || map.ReadInt32( trail + 0xe8 ) != 2 ) continue;
			int first = Block( trail + 0xe8 ), last = first + 0x40;
			Color Tint( int state ) => new( Float( state + 32 ), Float( state + 36 ), Float( state + 40 ), Float( state + 28 ) );
			return new ContrailAppearance
			{
				TexturePath = Host.AssetPath( map, texture.TagPath, "vtex" ),
				PointRate = Float( trail + 4 ), Repeats = Float( trail + 0x1c ),
				Transition = Float( first + 8 ), Lifetime = Float( first + 8 ) + Float( last ),
				Width = Float( first + 24 ) * 120, StartColor = Tint( first ), EndColor = Tint( last )
			};
		}
		return null;
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		base.Shutdown();
	}
}
