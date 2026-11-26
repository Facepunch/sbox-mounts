using Halo2.Blam;
using Sandbox.Mounting;
using System;

namespace Halo2;

internal sealed partial class HandheldWeaponLoader( MapFile source, TagEntry tag, string title, string hold ) : WeaponLoader( source, tag )
{
	internal string Title => title;
	HandheldWeaponLoader reticleSource;

	// Original HUD pixel extents, in MCC hud_reticles bitmap-page order.
	static readonly Vector2[] reticleSizes = [
		new(55,55), new(46,34), new(54,53), new(53,45), new(45,45), new(28,50), new(56,17), new(7,7),
		new(25,25), new(63,63), new(55,55), new(58,34), new(49,54), new(56,33), new(16,5), new(45,45),
		new(55,55), new(35,35), new(51,62), new(13,5), new(55,53), new(55,53), new(9,9), new(27,27),
		new(23,17), new(27,27), new(27,23), new(23,23), new(14,25), new(28,9), new(13,13), new(32,31),
		new(28,28), new(27,16), new(25,27), new(28,17), new(27,27), new(17,17), new(25,31), new(27,27),
		new(27,27), new(52,55), new(26,28), new(42,41), new(23,23), new(62,63), new(31,31), new(63,63),
		new(31,31), new(35,19), new(17,9), new(51,27), new(25,13), new(48,17), new(24,9), new(49,29),
		new(25,15), new(39,26), new(21,14)
	];

	static readonly Vector2[] weakReticleSizes = [
		new(3,3), new(3,3), new(55,55), new(27,27), new(54,46), new(27,23), new(45,45), new(23,23),
		new(56,17), new(28,9), new(25,25), new(13,13), new(55,55), new(28,28), new(35,35), new(17,17),
		new(13,5), new(9,9), new(35,19), new(17,9), new(39,26), new(21,14), new(49,54), new(25,27),
		new(45,36), new(21,17)
	];

	ReticleLayout ReadReticle( string widget, int layout )
	{
		if ( HudPage( widget, layout ) is not {} page ) return null;
		var sizes = page.Bitmap.TagPath switch
		{
			@"ui\hud\bitmaps\new_hud\crosshairs\hud_reticles" => reticleSizes,
			@"ui\hud\bitmaps\new_hud\crosshairs\hud_weak_spot_reticles" => weakReticleSizes,
			_ => null
		};
		if ( sizes is null || (uint)page.Index >= sizes.Length ) return null;
		int row = page.Row;
		int offset = row + 0x34 + layout * 4;
		int registration = row + 0x40 + layout * 8;
		var size = sizes[page.Index];
		return new( size, new Vector2( Short( offset ), Short( offset + 2 ) ) -
			new Vector2( MathF.Floor( size.x * Float( registration ) ), MathF.Floor( size.y * Float( registration + 4 ) ) ),
			Short( row + 0x10 ), (Short( row + 0x12 ) & 8) != 0 );
	}

	ReticleLayout[] ReadReticles( string widget ) => [ReadReticle( widget, 0 ), ReadReticle( widget, 1 ), ReadReticle( widget, 2 )];

	internal void FindReticleSource( MapFile candidate, TagEntry definition )
	{
		if ( reticleSource is not null ) return;
		var loader = candidate == map ? this : new HandheldWeaponLoader( candidate, definition, title, hold );
		if ( loader.HudPage( "crosshair", false ).HasValue ) reticleSource = loader;
	}

	internal static HandheldWeaponLoader ForTag( MapFile map, TagEntry weapon, string name ) => name switch
	{
		"head" => new( map, weapon, "Oddball", WeaponHolds.Melee ),
		"head_sp" => new( map, weapon, "Campaign Skull", WeaponHolds.Melee ),
		"ball" => new( map, weapon, "Oddball", WeaponHolds.Melee ),
		"juggernaut_powerup" => new( map, weapon, "Juggernaut Power-up", WeaponHolds.Melee ),
		"flag" => new( map, weapon, "Flag", WeaponHolds.Melee ),
		"assault_bomb" => new( map, weapon, "Assault Bomb", WeaponHolds.Melee ),
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
		"smg_silenced" => new( map, weapon, "Silenced SMG", WeaponHolds.Smg ),
		"gpmg" => new( map, weapon, "General-Purpose Machine Gun", WeaponHolds.Rifle ),
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

	Dictionary<string, TagEntry> contactEffects;
	Dictionary<string, string[]> contactSounds;

	Dictionary<string, BulletContact> bulletContacts, chargedContacts;

	Dictionary<string, BulletContact> LoadBulletContacts( TagEntry projectile, Dictionary<string, string> parents, HashSet<TagEntry> tagsToRegister )
	{
		int materials = Address( map.Tags.First( t => t.PrimaryClass == "gtam" ) ) + 0x150;
		int responses = Address( projectile ) + 0x19c;
		var rows = new Dictionary<string, int>();
		for ( int i = 0, row = Block( responses ); i < map.ReadInt32( responses ); i++, row += 0x58 )
			if ( map.ResolveStringId( map.ReadInt32( row + 12 ) ) is { Length: > 0 } name )
				rows.TryAdd( name, row );

		var banks = new Dictionary<int, Dictionary<string, BulletImpact>>();
		BulletImpact Impact( int response, string material )
		{
			int action = response < 5 ? 15 + response : 20;
			if ( !banks.TryGetValue( action, out var bank ) )
			{
				var effects = ReadProjectileBank( materials, projectile, action, true, parents );
				var sounds = ReadProjectileSounds( materials, projectile, action, parents );
				tagsToRegister.UnionWith( effects.Values.SelectMany( tags => tags ) );
				banks[action] = bank = effects.Keys.Union( sounds.Keys ).ToDictionary( n => n, n => new BulletImpact(
					effects.GetValueOrDefault( n )?.Select( tag => Host.AssetPath( map, tag.TagPath, "prefab" ) ).ToArray(), sounds.GetValueOrDefault( n ) ) );
			}
			return bank.GetValueOrDefault( material );
		}

		var contacts = new Dictionary<string, BulletContact>();
		foreach ( var material in parents.Keys )
		{
			var name = material;
			for ( int depth = 0; name is { Length: > 0 } && depth < parents.Count; depth++ )
			{
				if ( rows.TryGetValue( name, out int row ) )
				{
					float chance = Float( row + 0x18 );
					int primary = Short( row + 2 ), alternate = Short( row + 0x14 );
					contacts[material] = new( Impact( primary, material ), chance, chance > 0 ? Impact( alternate, material ) : null )
					{
						Shard = Name == "needler" ? new( (ShardResponse)primary, (ShardResponse)alternate, Short( row + 0x16 ),
							new( Float( row + 0x1c ), Float( row + 0x20 ) ), Float( row + 0x38 ), new( Float( row + 0x50 ), Float( row + 0x54 ) ),
							Float( row + 0x48 ), Float( row + 0x4c ) * 120 ) : null
					};
					break;
				}
				if ( !parents.TryGetValue( name, out name ) ) break;
			}
		}
		return contacts;
	}

	void LoadContactBanks( Dictionary<string, string> materialParents )
	{
		if ( !HasImpactBlast ) return;
		var projectile = Reference( PrimaryBarrel + 0x8c );
		int header = Address( projectile );
		if ( Reference( header + 0x54 ) is null || map.ReadInt32( header + 0x19c ) != 1 ) return;
		int response = Block( header + 0x19c );
		// Terminal contacts use action 15. Reflection has a separate bank.
		if ( Short( response + 2 ) != 0 || Short( response + 0x14 ) is not (0 or 4) ) return;
		int materials = Address( map.Tags.First( t => t.PrimaryClass == "gtam" ) ) + 0x150;
		contactEffects = ReadMaterialBank( materials, projectile, 0x54, 15, 16, materialParents );
		contactSounds = ReadProjectileSounds( materials, projectile, 15, materialParents );
	}

	internal void RegisterEffects( MountContext context, HashSet<string> registered, Dictionary<string, string> materialParents )
	{
		var projectile = HasBarrel ? Reference( PrimaryBarrel + 0x8c ) : null;
		if ( projectile is not null && ((map.ReadInt32( Address( projectile ) + 0xbc ) & 32) != 0
			|| Name is "smg" or "smg_silenced" or "gpmg" or "plasma_pistol" or "plasma_rifle" or "brute_plasma_rifle" or "needler") )
		{
			var tags = new HashSet<TagEntry>();
			bulletContacts = LoadBulletContacts( projectile, materialParents, tags );
			if ( Name == "plasma_pistol" )
			{
				int secondary = Block( Header + 0x2d0 ) + Short( PrimaryTrigger + 10 ) * 0xec;
				chargedContacts = LoadBulletContacts( Reference( secondary + 0x8c ), materialParents, tags );
			}
			foreach ( var tag in tags ) Register( context, tag );
		}
		LoadContactBanks( materialParents );
		if ( contactEffects is not null )
			foreach ( var effect in contactEffects.Values.Distinct() ) Register( context, effect );
		Register( context, DetonationEffect );
		if ( HasProjectileEffects ) RegisterProjectileEffects( context, registered );
		if ( Name == "needler" ) Register( context, Reference( Address( Reference( PrimaryBarrel + 0x8c ) ) + 0x110 ) );

		void Register( MountContext mount, TagEntry tag )
		{
			if ( tag is { PrimaryClass: "effe" } && registered.Add( ImpactPath( tag ) ) )
				mount.Add( ResourceType.PrefabFile, ImpactPath( tag ), new ImpactLoader( this, tag, includeSounds: true ) { Flags = ResourceFlags.DeveloperOnly | ResourceFlags.Effect } );
		}
	}

	bool HasProjectileEffects => HasImpactBlast || Name is "big_needler_handheld" or "phantom_turret_handheld" or "needler" or "plasma_pistol" or "plasma_rifle" or "brute_plasma_rifle";
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
		bool split = name.StartsWith( "split_", StringComparison.Ordinal );
		bool quarter = name.StartsWith( "quarter_", StringComparison.Ordinal );
		if ( split ) name = name[6..];
		if ( quarter ) name = name[8..];
		var widget = name switch
		{
			"crosshair.vtex" => "crosshair",
			"crosshair_weakspot.vtex" => "crosshair_weakspot",
			"lockable.vtex" when Name == "rocket_launcher" => "lockable",
			"locked.vtex" when Name == "rocket_launcher" => "locked",
			"locking.vtex" when Name == "rocket_launcher" => "locking",
			_ => null
		};
		var source = widget is "crosshair" or "crosshair_weakspot" ? reticleSource ?? this : this;
		if ( widget is null || source.HudPage( widget, quarter ? 2 : split ? 1 : 0 ) is not {} page ) return null;
		return new Halo2TextureLoader( source.map, page.Bitmap, page.Index, hudMask: true );
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
				"flag" or "ball" or "assault_bomb" => ConfigurePickup<ObjectiveWeapon>( root, world, title ),
				"head_sp" => ConfigurePickup<CampaignSkull>( root, world, title ),
				"juggernaut_powerup" => ConfigurePickup<JuggernautPowerup>( root, world, title ),
				_ => ConfigurePickup<MeleeWeapon>( root, world, title )
			};
			item.HoldType = hold;
			item.RigDirectory = $"mount://{Host.Ident}/weapons/{Name}";
			if ( reticleSource is not null )
			{
				item.Reticles = reticleSource.ReadReticles( "crosshair" );
				item.WeakReticles = reticleSource.ReadReticles( "crosshair_weakspot" );
			}
			item.SpartanModel = (Host.GetByPath( $"{item.RigDirectory}/first_person.vmdl" ) as Halo2ModelLoader)?.FirstPersonWeaponPath;
			item.EliteModel = (Host.GetByPath( $"{item.RigDirectory}/first_person_elite.vmdl" ) as Halo2ModelLoader)?.FirstPersonWeaponPath;
			item.ViewOffset = new Vector3( Float( Header + 0x308 ), Float( Header + 0x30c ), Float( Header + 0x310 ) ) * 120;
			item.DeployTime = Float( Header + 0x13c );
		}
		return prefab = builder.Create();
	}

	EnergySword CreateSword( GameObject root, string world )
	{
		var sword = ConfigurePickup<EnergySword>( root, world, title );
		sword.BladeSound = ReadSoundTrack( "blade_activate" );
		for ( int i = 0, row = Block( Header + 0x94 ); i < map.ReadInt32( Header + 0x94 ); i++, row += 24 )
		{
			if ( Reference( row ) is not { PrimaryClass: "effe" } effectTag ||
				map.ResolveStringId( map.ReadInt32( row + 16 ) ) != "turned_on" ) continue;
			var marker = map.ResolveStringId( map.ReadInt32( row + 8 ) );
			int header = Address( effectTag );
			sword.GlowEvents = Enumerable.Range( 0, map.ReadInt32( header + 20 ) ).Select( index =>
			{
				int ev = Block( header + 20 ) + index * 56;
				return new BladeGlowEvent( Float( ev + 4 ), Float( ev + 16 ), ReadMuzzleFlash( effectTag, index, marker ) );
			} ).ToArray();
			sword.GlowLoopStart = Short( header + 4 );
		}
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

		sword.ReadyParticles = ReadMuzzleFlash( ready );
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
		if ( gun is MagazineWeapon magazine && !gun.PrimaryAutomatic && magazine.BurstLength == 1 && rate == 0 )
		{
			float ticks = Float( barrel + 0x20 ) * 30;
			int firstStage = (int)MathF.Floor( (1 - Float( barrel + 0x24 )) * ticks );
			int secondStage = (int)MathF.Floor( ticks ) - firstStage;
			if ( secondStage > 0 )
			{
				gun.PrimaryDelay = Weapon.CooldownSeconds( 2 + Math.Max( 1, firstStage ) + secondStage );
				if ( (map.ReadInt32( barrel ) & (1 << 11)) != 0 ) magazine.RecoveryBufferTicks = secondStage;
			}
		}
		var sounds = FiringSounds( barrel ).ToArray();
		if ( sounds.Length > 0 )
		{
			gun.AttackSound = sounds[0].Sound;
			gun.AttackSoundMarker = sounds[0].Marker;
		}
		if ( map.ReadInt32( barrel + 0xe4 ) == 1 )
		{
			if ( sounds.Length > 1 ) gun.AdditionalFiringSounds = sounds[1..];
			gun.PrimaryFlash = ReadMuzzleFlash( Reference( Block( barrel + 0xe4 ) + 4 ) );
		}
		ConfigureKick( gun, barrel );
		gun.Ballistics = ReadBallistics( gun.Ballistics, barrel, projectile, damage );
		gun.ErrorProfile = new( new( Float( barrel + 0xdc ), Float( barrel + 0xe0 ) ),
			new( Float( barrel + 0x50 ), Float( barrel + 0x54 ) ), Float( barrel + 0x58 ).RadianToDegree(),
			new( Float( barrel + 0x5c ).RadianToDegree(), Float( barrel + 0x60 ).RadianToDegree() ),
			(map.ReadInt32( Header + 0x12c ) & (1 << 27)) != 0 );
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
				ReadBlast( Reference( projectile + 0x108 ) ), ReadBlast( Reference( projectile + 0x100 ) ), Reference( barrel + 0x8c ).TagPath,
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
						(int)MathF.Round( Float( projectile + 0xd4 ) * 30 ) )
					{
						Sounds = ReadProjectileSounds( Address( map.Tags.First( t => t.PrimaryClass == "gtam" ) ) + 0x150, Reference( barrel + 0x8c ), 19 )
					};
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
		if ( HasProjectileEffects ) gun.ProjectileVolume = ProjectileEffectPath( Reference( barrel + 0x8c ) );
		if ( contactEffects is not null )
		{
			gun.ProjectileContactEffects = contactEffects.ToDictionary( e => e.Key, e => e.Value is {} tag ? Host.AssetPath( map, tag.TagPath, "prefab" ) : null );
			gun.ProjectileContactSounds = contactSounds;
		}
		gun.BulletContacts = bulletContacts;
		gun.Trail = ReadTrail( projectile );
		int flags = map.ReadInt32( projectile + 0xbc );
		gun.Orientation = ReadOrientation( flags );
		gun.TargetLeading = Float( projectile + 0x198 );
		gun.Instantaneous = (flags & 32) != 0;
		gun.PelletSpeed = Float( projectile + 0x17c ) * 120;
		gun.SpeedProfile = ReadSpeedProfile( projectile );
		gun.MinimumPelletAngle = Float( barrel + 0x70 ).RadianToDegree();
		gun.AirGravityScale = Float( projectile + 0x164 );
		gun.WaterGravityScale = Float( projectile + 0x170 );
		bool falloff = (flags & 16) != 0;
		gun.DamageFalloffRange = falloff ? Float( projectile + 0x16c ) * 120 : 0;
		gun.UnderwaterDamageFalloffRange = falloff ? Float( projectile + 0x178 ) * 120 : 0;
		gun.MinimumDamage = Float( damage + 0x1c );
		gun.PelletBodyScales = BodyDamageScales( Address( map.Tags.First( t => t.PrimaryClass == "gtam" ) ), damage );
		gun.PelletExplosive = (map.ReadInt32( damage + 0x14 ) & 0x20) != 0;
		gun.CanHeadshot = (map.ReadInt32( damage + 0x14 ) & 2) != 0;
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
			pistol.ChargedContacts = chargedContacts;
			pistol.ChargeSound = ReadSoundTrack( "primary_charged" );
			int secondary = Block( Header + 0x2d0 ) + Short( PrimaryTrigger + 10 ) * 0xec;
			int bolt = Address( Reference( secondary + 0x8c ) );
			int impact = Address( Reference( bolt + 0x148 ) );
			int flags = map.ReadInt32( bolt + 0xbc );
			bool fades = (flags & 16) != 0;
			pistol.ChargedShot = new( ReadBallistics( pistol.Ballistics, secondary, bolt, impact ), Float( bolt + 0x17c ) * 120,
				Float( secondary + 0x70 ).RadianToDegree(), Float( bolt + 0x164 ), Float( bolt + 0x170 ), Float( impact + 0x1c ),
				fades ? Float( bolt + 0x16c ) * 120 : 0, fades ? Float( bolt + 0x178 ) * 120 : 0,
				BodyDamageScales( Address( map.Tags.First( t => t.PrimaryClass == "gtam" ) ), impact ), (map.ReadInt32( impact + 20 ) & 32) != 0,
				ReadTrail( bolt ), Float( secondary + 0xa4 ), Float( secondary + 0xa8 ), FiringSound( secondary, out var chargedMarker ), chargedMarker,
				Float( bolt + 0x188 ), Float( Header + 0x208 ), Float( Header + 0x20c ) * 120 )
			{
				SpeedProfile = ReadSpeedProfile( bolt ), Orientation = ReadOrientation( flags ), TargetLeading = Float( bolt + 0x198 ), Volume = ProjectileEffectPath( Reference( secondary + 0x8c ) ),
				CanHeadshot = (map.ReadInt32( impact + 0x14 ) & 2) != 0
			};
			gun = pistol;
		}
		else gun = ConfigurePickup<BatteryWeapon>( root, world, title );
		gun.UsesAmmo = gun.UsesClips = false;
		gun.FiringTrack = ReadSoundTrack( "audio_firing" );
		gun.OverheatTrack = ReadSoundTrack( "overheated" );
		gun.VentParticles = ReadMuzzleFlash( Reference( Header + 0x16c ) );
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

	MuzzleFlashDefinition[] ReadMuzzleFlash( TagEntry effect, int selectedEvent = -1, string attachmentMarker = null )
	{
		if ( effect is not { PrimaryClass: "effe", DataSize: > 0 } ) return null;
		int header = Address( effect );
		if ( map.ReadInt32( header + 20 ) == 0 ) return null;
		var markers = new Dictionary<string, List<MuzzleFlashLayer>>();
		var lights = new Dictionary<string, List<PelletImpactEffect.ImpactLight>>();
		float initialAdvance = (map.ReadInt32( header ) & 2) != 0 ? 1 : 0;
		float start = 0;
		for ( int i = 0, ev = Block( header + 20 ); i < map.ReadInt32( header + 20 ); i++, ev += 56 )
		{
			if ( selectedEvent >= 0 && i != selectedEvent ) continue;
			start += Float( ev + 8 );
			float duration = Float( ev + 16 );
			ReadSystems( ev + 48, start, new Vector2( duration, Float( ev + 20 ) ) );
			if ( start == 0 )
			{
				for ( int j = 0, part = Block( ev + 24 ); j < map.ReadInt32( ev + 24 ); j++, part += 56 )
				{
					if ( ReadLight( part ) is not {} light ) continue;
					int index = Short( part + 4 );
					if ( (uint)index >= map.ReadInt32( header + 12 ) ) continue;
					var marker = map.ResolveStringId( map.ReadInt32( Block( header + 12 ) + index * 4 ) );
					if ( marker is null ) continue;
					if ( !lights.TryGetValue( marker, out var list ) ) lights[marker] = list = new();
					list.Add( light );
				}
			}
			start += duration;
		}
		return markers.Keys.Union( lights.Keys ).Select( marker => new MuzzleFlashDefinition( marker,
			markers.GetValueOrDefault( marker )?.ToArray() ?? [] ) { Lights = lights.GetValueOrDefault( marker ) ?? new() } ).ToArray();

		void ReadSystems( int systems, float start, Vector2 duration )
		{
			for ( int i = 0, row = Block( systems ); i < map.ReadInt32( systems ); i++, row += 56 )
			{
				var particle = Reference( row );
				bool additiveSmoke = particle?.TagPath is @"effects\generic\smoke\smoke_fiery_small" or @"effects\generic\smoke\smoke_fiery" or @"effects\generic\smoke\smoke_wispy_small_additive"
					or @"effects\generic\energy\smoke_c_plasma_red_large" or @"effects\generic\energy\smoke_white_c_generic";
				bool soft = particle?.TagPath == @"effects\generic\smoke\smoke_soft";
				bool energySmoke = particle?.TagPath is @"effects\generic\energy\smoke_c_generic" or @"effects\generic\energy\plasma_overheat";
				bool shieldFlash = particle?.TagPath == @"effects\generic\sparks\flash_h_shield_impact";
				bool beam = effect.TagPath == @"effects\scenarios\objects\covenant\military\scarab\main_gun_beam";
				bool exhaustSmoke = (beam || effect.TagPath.EndsWith( @"\rocket_exhaust", StringComparison.Ordinal ) || effect.TagPath.EndsWith( @"\flak_bolt_armed", StringComparison.Ordinal ) || effect.TagPath == @"effects\objects\weapons\grenade\frag_grenade\smoke_trail") && Short( row + 12 ) == 0;
				bool smoke = exhaustSmoke || additiveSmoke || soft || energySmoke || particle?.TagPath is @"effects\objects\weapons\rifle\battle_rifle\smoke" or @"effects\objects\weapons\rifle\smg\muzzle_smoke"
					or @"effects\objects\vehicles\warthog\chaingun\muzzle_smoke";
				bool casing = particle?.TagPath is @"effects\objects\weapons\pistol\magnum\magnum_casings" or @"effects\objects\weapons\rifle\battle_rifle\br_casings"
					or @"effects\objects\weapons\rifle\sniper_rifle\sr_casing_par" or @"effects\objects\vehicles\warthog\chaingun\chaingun_casing_par";
				bool blade = particle?.TagPath is @"effects\objects\weapons\melee\energy_blade\glow_blade" or @"effects\objects\weapons\melee\energy_blade\blade_charge";
				bool ignition = particle?.TagPath is @"effects\generic\energy\energy_blade_activate" or @"effects\generic\energy\plasma_wispy";
				bool exhaustFlame = particle?.TagPath is @"effects\objects\weapons\support_high\rocket_launcher\rocket_flame" or @"effects\objects\weapons\support_high\rocket_launcher\rocket_flame_core";
				bool ember = particle?.TagPath == @"effects\generic\energy\ember";
				bool flare = ember || exhaustFlame || blade || ignition || particle?.TagPath is @"effects\generic\weapon_flares\muzzle_flash_long" or @"effects\generic\weapon_flares\muzzle_flash_long_2" or @"effects\generic\weapon_flares\muzzle_flash_center"
					or @"effects\objects\vehicles\warthog\chaingun\muzzle_flash" or @"effects\objects\vehicles\warthog\chaingun\muzzle_flash_long"
					or @"effects\generic\flares\flare_large" or @"effects\generic\flares\ring" or @"effects\generic\flares\burst_soft" or @"effects\generic\flares\flash_shield"
					or @"effects\generic\energy\flash_c_generic_big" or @"effects\generic\energy\flash_c_generic_long" or @"effects\generic\energy\c_muzzle_flash_red_large"
					or @"effects\generic\sparks\electric_arcs" or @"effects\generic\energy\energy_bolt_trail";
				if ( !smoke && !shieldFlash && !casing && !flare && particle?.TagPath is not (@"effects\generic\energy\flash_c_generic" or @"effects\generic\energy\flash_c_generic_med" or @"effects\generic\energy\plasma_electric_burst" or @"effects\generic\energy\tracer_c_generic"
					or @"effects\objects\weapons\rifle\smg\fire_burst_center" or @"effects\objects\weapons\rifle\smg\fire_burst_long"
					or @"effects\objects\weapons\rifle\battle_rifle\muzzle_flash" or @"effects\objects\weapons\rifle\battle_rifle\muzzle_flash_center") ) continue;
				int definition = Address( particle );
				int binding = Block( Block( definition + 140 ) + 4 );
				var bitmap = map.GetTag( map.ReadInt32( binding ) & 0xffff );
				if ( bitmap is null ) continue;
				var texture = Host.AssetPath( map, bitmap.TagPath, "vtex" );
				var pivot = new Vector2( 0.5f, 0.5f );
				var aspect = Vector2.One;
				int imageIndex = map.ReadInt32( binding + 4 );
				if ( Short( Address( bitmap ) ) == 3 && (uint)Short( definition + 8 ) < map.ReadInt32( Address( bitmap ) + 60 ) &&
					ReadAnimatedFrames( definition ) is [var frame, ..] )
				{
					texture += $"/sequence_{Short( definition + 8 )}.vtex";
					pivot = frame.Pivot * frame.InverseSize;
					aspect = frame.Aspect;
				}
				else if ( BitmapTag.Read( map, bitmap, imageIndex ) is {} image )
				{
					if ( imageIndex > 0 ) texture += $"/{imageIndex}.vtex";
					aspect.x = (float)image.Width / image.Height;
				}
				var marker = attachmentMarker ?? map.ResolveStringId( map.ReadInt32( Block( header + 12 ) + 4 * map.ReadInt32( row + 8 ) ) );
				if ( marker is null ) continue;
				if ( !markers.TryGetValue( marker, out var layers ) ) markers[marker] = layers = new();
				CasingSounds contacts = null;
				if ( casing )
				{
					var globals = map.Tags.First( t => t.PrimaryClass == "gtam" );
					var sounds = ReadMaterialBank( Address( globals ) + 0x150, particle, 0x6c, 10, 8 );
					var cues = new List<CuePlayback>();
					contacts = new CasingSounds { Casings = ReadSoundBank( sounds, cues, new() ), Cues = cues.ToArray() };
				}
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
					Vector2 BirthBounds( int property )
					{
						if ( Short( property ) != 0 || Short( property + 4 ) != 0 ) return Bounds( property );
						// Age is zero when the particle is born. Its range input can still vary.
						var curve = ReadRange( property, 1 );
						return new( curve.Evaluate( 0, 0 ), curve.Evaluate( 0, 1 ) );
					}
					float Motion( int parameter )
					{
						if ( Reference( emitter ) is not {} movement || map.ReadInt32( Address( movement ) + 12 ) == 0 ) return 0;
						int control = Block( Address( movement ) + 12 );
						return Float( Block( Block( control + 4 ) + parameter * 20 + 12 ) + 4 );
					}
					var births = Bounds( emitter + 8 );
					if ( blade && Short( emitter + 8 ) == 9 )
						births = Vector2.One * ReadRange( emitter + 8, 1 ).Evaluate( 1, 0 );
					layers.Add( new( texture,
						new Vector2( 1 - pivot.x, pivot.y ), aspect, exhaustSmoke || ignition || particle.TagPath == @"effects\generic\sparks\electric_arcs" ? BirthBounds( emitter + 24 ) : Bounds( emitter + 24 ),
						ReadRange( emitter + 72, smoke || exhaustFlame ? 120 : 120 * Bounds( definition + 60 ).x ), ReadRange( emitter + 56, 1 ),
						map.ReadInt32( definition + 100 ) > 0 ? ReadRange( definition + 92, 1 ) : default,
						Tint( 4 ), (map.ReadInt32( color ) & 0xff00) == 0x2000 ? Tint( 16 ) : Tint( 4 ),
						offset, direction, Short( definition + 4 ) == 1, Short( row + 18 ), Short( row + 12 ) == 1,
						births, Short( emitter + 8 ), j == 0, Bounds( emitter + 124 ) * 120, (ignition ? BirthBounds( emitter + 40 ) : Bounds( emitter + 40 )) * 120, Short( emitter + 72 ) == 2,
						smoke || shieldFlash ? new( ReadRange( definition + 60, 1 ), ReadRange( definition + 44, 1 ), (exhaustSmoke || additiveSmoke || soft) ? Vector2.One : Bounds( emitter + 104 ), beam || additiveSmoke || energySmoke || shieldFlash, Motion( 0 ), Motion( 1 ) ) : null, contacts, casing || smoke || blade || ignition ? Bounds( emitter + 140 ).x : 0,
						flare || soft || additiveSmoke || (!smoke && !casing && duration.y > 1.001f / 30) ? ReadRange( emitter + 104, 1 ) : null,
						duration.y > 1.001f / 30 || start > 0 ? new( start - initialAdvance, duration ) : null )
					{
						ExhaustSmoke = exhaustSmoke ? new( Short( emitter + 8 ) == 9 ? ReadRange( emitter + 8, 1 ) : null,
							Short( emitter + 40 ) == 9 ? ReadRange( emitter + 40, 120 ) : null, ReadRange( emitter + 140, 1 ),
							ReadRange( emitter + 104, 1 ), Short( emitter + 104 ) == 2, (Short( row + 22 ) & 0x80) != 0 ) : null,
						RandomCone = ember && Short( emitter + 140 ) == 15 ? ReadRange( emitter + 140, 1 ) : null,
						SingleParticle = (Short( row + 22 ) & 4) != 0,
						LineEmitter = Short( emitter + 120 ) == 9,
						Environment = (ParticleEnvironment)Short( row + 14 ),
						RandomRadiusScale = exhaustFlame ? ReadRange( definition + 60, 1 ) : null,
						ParticleTint = Short( emitter + 88 ) == 3 && map.ReadSpan( color, 1 )[0] == 3 &&
							Short( color + 2 ) is >= 8 and <= 10 && map.ReadSpan( color + 1, 1 )[0] == 0x20
							? ReadRange( emitter + 88, 1, fraction: true ) : null
					} );
				}
			}
		}
	}

	WeaponSoundTrack ReadSoundTrack( string function )
	{
		for ( int i = 0, row = Block( Header + 0x94 ); i < map.ReadInt32( Header + 0x94 ); i++, row += 24 )
			if ( map.ResolveStringId( map.ReadInt32( row + 16 ) ) == function && ReadSoundTrack( row ) is {} track ) return track;
		return null;
	}

	WeaponSoundTrack ReadSoundTrack( int attachment )
	{
		if ( Reference( attachment ) is not { PrimaryClass: "dnsl", DataSize: > 0 } sound ) return null;
		int tracks = Address( sound ) + 0x24;
		if ( map.ReadInt32( tracks ) != 1 ) return null;
		int track = Block( tracks );
		SoundEvent Cue( int offset ) => Reference( track + offset ) is { PrimaryClass: "!dns" } tag
			? GameResource.Load<SoundEvent>( Host.AssetPath( map, tag.TagPath, "sound" ) ) : null;
		return new( map.ResolveStringId( map.ReadInt32( attachment + 8 ) ), Cue( 0x14 ), Cue( 0x1c ), Cue( 0x24 ),
			MathF.Pow( 10, Float( track + 8 ) / 20 ), Float( track + 16 ) );
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
				Marker = map.ResolveStringId( map.ReadInt32( row + 8 ) ),
				PointRate = Float( trail + 4 ), Repeats = Float( trail + 0x1c ),
				Transition = Float( first ) + Float( first + 8 ), Lifetime = Float( first ) + Float( first + 8 ) + Float( last ),
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
