using Halo2.Blam;
using Sandbox.Mounting;
using System;

namespace Halo2;

internal sealed partial class ShotgunLoader( MapFile source, TagEntry tag ) : WeaponLoader( source, tag )
{
	internal const string PrefabPath = "weapons/shotgun.prefab";
	internal const string AmmoPath = "ammo/shotgun_shells.ammo";
	internal static BaseAmmoResource LoadShells() => GameResource.Load<BaseAmmoResource>( $"mount://{Halo2Mount.MountIdent}/{AmmoPath}" );
	internal const string RigPath = "weapons/shotgun/first_person.vmdl";
	internal const string EliteRigPath = "weapons/shotgun/first_person_elite.vmdl";
	internal const string ReticlePath = "weapons/shotgun/reticle.vtex";
	internal const string AmmoMeterName = "ammo_meter.vtex";
	internal const string AmmoBackgroundName = "ammo_background.vtex";
	internal const string EmptyAmmoBackgroundName = "ammo_background_out.vtex";
	internal const string IconPath = "weapons/shotgun/icon.vtex";
	internal const string SplitReticleName = "split_reticle.vtex";
	internal const string SplitWeakReticleName = "split_weak_reticle.vtex";
	internal const string WeakReticleName = "weak_reticle.vtex";
	internal const string MuzzleFlashName = "muzzle_flash.vtex";
	internal const string LongFlashName = "muzzle_flash_long.vtex";
	internal const string CasingName = "casing.vtex";
	internal const string SmokeName = "smoke_soft.vtex";
	internal const string FierySmokeName = "smoke_fiery.vtex";
	internal const string RocksName = "rocks.vtex";
	internal const string GlassName = "glass_breakable.vtex";
	internal const string MetalChipsName = "metal_chips.vtex";
	internal const string DriftName = "smoke_drift.vtex";
	internal const string RubberName = "rubber_impact.vtex";
	internal const string MistName = "blood_mist_impact.vtex";
	internal const string ClothName = "debris_shirt.vtex";
	internal const string LeafName = "plant_leafy.vtex";
	internal const string GrassName = "plant_grassy.vtex";
	internal const string BurstName = "impact_burst.vtex";
	internal const string SoftBurstName = "impact_burst_soft.vtex";
	internal const string ClumpsName = "impact_clumps.vtex";
	internal const string RippleName = "impact_ripple.vtex";
	internal const string ShieldName = "impact_energy.vtex";
	internal const string WoodName = "plant_wood.vtex";
	internal const string DirtBurstName = "dirt_burst.vtex";
	internal const string DirtPuffName = "dirt_puff.vtex";
	internal const string DirtConeName = "dirt_burst_cone.vtex";
	internal const string DustName = "smoke_impact.vtex";
	internal const string ArcName = "electric_arcs.vtex";
	internal const string SmallFierySmokeName = "smoke_fiery_small.vtex";
	internal const string SprayName = "impact_spray.vtex";
	internal const string UnlitSprayName = "impact_spray_unlit.vtex";
	internal const string ThickSprayName = "impact_spray_thick_unlit.vtex";
	internal const string UnlitImpactSmokeName = "smoke_soft_small.vtex";
	internal const string ImpactSmokeName = "smoke_soft_small_lit.vtex";
	internal const string SparkTrailName = "sparks_trail.vtex";
	internal const string SparksName = "sparks_animated.vtex";

	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = ResourceType.Texture;
		if ( name.Length == "digit_0.vtex".Length && name.StartsWith( "digit_", StringComparison.Ordinal )
			&& name.EndsWith( ".vtex", StringComparison.Ordinal ) && name[6] is >= '0' and <= '9' )
			return new HudDigitLoader( name[6] );
		if ( name == AmmoMeterName ) return HudBitmap( "ammo_meter_single" );
		if ( name == AmmoBackgroundName ) return HudBitmap( "weapon_background_single" );
		if ( name == EmptyAmmoBackgroundName ) return HudBitmap( "weapon_background_out" );
		if ( name == "ammo_background_elite.vtex" ) return HudBitmap( "weapon_background_single", elite: true );
		if ( name == "ammo_background_out_elite.vtex" ) return HudBitmap( "weapon_background_out", elite: true );
		bool eliteMeter = name.StartsWith( "ammo_meter_elite_", StringComparison.Ordinal );
		string meterPrefix = eliteMeter ? "ammo_meter_elite_" : "ammo_meter_";
		if ( name.StartsWith( meterPrefix, StringComparison.Ordinal ) && name.EndsWith( ".vtex", StringComparison.Ordinal )
			&& int.TryParse( name.AsSpan()[meterPrefix.Length..^5], out int shells ) )
		{
			int capacity = Short( Block( Header + 0x2c0 ) + 0xa );
			if ( capacity > 0 && shells >= 0 && shells <= capacity )
				return HudBitmap( "ammo_meter_single", meterFill: shells / (float)capacity, elite: eliteMeter );
		}
		if ( name == SplitReticleName ) return HudBitmap( "crosshair", split: true );
		if ( name == SplitWeakReticleName ) return HudBitmap( "crosshair_weakspot", split: true );
		if ( name == WeakReticleName ) return HudBitmap( "crosshair_weakspot" );
		var path = name switch
		{
			CasingName => @"effects\objects\weapons\rifle\shotgun\shotgun_casing",
			MuzzleFlashName => @"effects\generic\weapon_flares\muzzle_flash_center",
			LongFlashName => @"effects\generic\weapon_flares\muzzle_flash_long",
			SmokeName => @"effects\generic\smoke\smoke_soft",
			FierySmokeName => @"effects\generic\smoke\smoke_fiery",
			RocksName => @"effects\generic\solids\rocks",
			GlassName => @"effects\generic\solids\glass_breakable",
			MetalChipsName => @"effects\generic\solids\metal_chips",
			DriftName => @"effects\generic\smoke\smoke_drift",
			RubberName => @"effects\generic\solids\rubber_impact",
			MistName => @"effects\generic\smoke\blood_mist_impact",
			ClothName => @"effects\generic\solids\debris_shirt",
			LeafName => @"effects\generic\solids\plant_leafy",
			GrassName => @"effects\generic\solids\plant_grassy",
			BurstName => @"effects\generic\solids\impact_burst",
			SoftBurstName => @"effects\generic\solids\impact_burst_soft_unlit",
			ClumpsName => @"effects\generic\solids\impact_clumps",
			RippleName => @"effects\generic\solids\impact_ripple_unlit",
			ShieldName => @"effects\generic\energy\impact_energy",
			WoodName => @"effects\generic\solids\plant_wood",
			DirtBurstName => @"effects\generic\solids\dirt_burst",
			DirtPuffName => @"effects\generic\smoke\dirt_puff",
			DirtConeName => @"effects\generic\solids\dirt_burst_cone",
			DustName => @"effects\generic\smoke\smoke_impact",
			ArcName => @"effects\generic\sparks\electric_arcs",
			SmallFierySmokeName => @"effects\generic\smoke\smoke_fiery_small",
			ImpactSmokeName => @"effects\generic\smoke\smoke_soft_small_lit",
			SprayName => @"effects\generic\solids\impact_spray",
			UnlitSprayName => @"effects\generic\solids\impact_spray_unlit",
			ThickSprayName => @"effects\generic\solids\impact_spray_thick_unlit",
			UnlitImpactSmokeName => @"effects\generic\smoke\smoke_soft_small",
			SparkTrailName => @"effects\generic\sparks\sparks_trail",
			SparksName => @"effects\generic\sparks\sparks_animated_soft",
			_ => null
		};
		if ( path is null ) return null;
		var particle = map.Tags.FirstOrDefault( t => t.TagPath == path );
		if ( particle is null ) return null;
		int header = Address( particle );
		if ( map.ReadInt32( header + 140 ) == 0 ) return null;
		int bitmaps = Block( header + 140 ) + 4;
		if ( map.ReadInt32( bitmaps ) == 0 ) return null;
		var bitmap = map.GetTag( map.ReadInt32( Block( bitmaps ) ) & 0xffff );
		if ( bitmap is null ) return null;
		int flags = map.ReadInt32( header );
		if ( (flags & 0x10000) != 0 )
			return new Halo2TextureLoader( map, bitmap, spriteSequences: [[Halo2TextureLoader.SpriteBounds( map, header + 0xac )]] );

		if ( name is WoodName or LeafName or GrassName )
		{
			int sequence = Block( Address( bitmap ) + 60 ) + Short( header + 8 ) * 60;
			int sprites = Block( sequence + 52 );
			var frames = Enumerable.Range( 0, map.ReadInt32( sequence + 52 ) )
				.Select( i => new[] { Halo2TextureLoader.SpriteBounds( map, sprites + i * 32 + 8 ) } ).ToArray();
			return new Halo2TextureLoader( map, bitmap, Short( sprites ), spriteSequences: frames );
		}
		// Drift samples up to 0.8 * 17 including its blend neighbor. All 15 reachable frames share the first image.
		if ( name == DriftName )
		{
			int sprites = Block( Block( Address( bitmap ) + 60 ) + 52 );
			var frames = new Rect[15];
			for ( int i = 0; i < frames.Length; i++ )
				frames[i] = Halo2TextureLoader.SpriteBounds( map, sprites + i * 32 + 8 );
			return new Halo2TextureLoader( map, bitmap, Short( sprites ), spriteSequences: [frames] );
		}
		return Halo2TextureLoader.SpriteSheet( map, bitmap, (flags & 8) == 0 );
	}

	(CurveRange Radius, CurveRange Alpha) SmokeCurves( bool firstPerson )
	{
		int barrel = Block( Header + 0x2d0 );
		int systems = Block( FiringEvent( barrel ) + 48 );
		int emitter = Block( systems + 56 * (firstPerson ? 6 : 2) + 48 );
		return (ReadRange( emitter + 72, 120 ), ReadRange( emitter + 104, 1 ));
	}

	int FiringEvent( int barrel ) => Block( Address( Reference( Block( barrel + 0xe4 ) + 4 ) ) + 20 );

	int HudColor( int postprocess, int curve, bool elite )
	{
		int functions = Block( postprocess + 60 );
		return map.ReadInt32( Block( functions + curve * 20 + 12 ) + (elite ? 16 : 4) );
	}

	AmmoTextColors ReserveColors( bool elite )
	{
		int widgets = Address( Reference( Header + 0x2b0 ) ) + 16;
		int rows = Block( widgets );
		Color loaded = Color.Transparent, empty = Color.Transparent;
		for ( int i = 0; i < map.ReadInt32( widgets ); i++ )
		{
			int row = rows + i * 84;
			var name = map.ResolveStringId( map.ReadInt32( row ) );
			if ( name is not ("bullets_single" or "bullets_out") || Reference( row + 0x20 ) is not {} shader ) continue;
			int postprocess = Block( Address( shader ) + 0x20 );
			int constants = postprocess + 12;
			if ( map.ReadInt32( constants ) < 6 || map.ReadInt32( postprocess + 60 ) == 0 ) continue;
			// The text pass tints white glyphs with the first color and the sixth register's alpha.
			int data = Block( constants ), rgb = HudColor( postprocess, 0, elite );
			var color = Color.FromBytes( (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)(map.ReadInt32( data + 20 ) >> 24) );
			if ( name == "bullets_single" ) loaded = color;
			else empty = color;
		}
		return new( loaded, empty );
	}

	internal Halo2TextureLoader HudBitmap( string widget, bool split = false, float? meterFill = null, bool elite = false )
	{
		int hud = Address( Reference( Header + 0x2b0 ) );
		int widgets = Block( hud + 8 );
		for ( int i = 0; i < map.ReadInt32( hud + 8 ); i++ )
		{
			int row = widgets + i * 100;
			if ( map.ResolveStringId( map.ReadInt32( row ) ) != widget ) continue;
			var bitmap = Reference( row + 0x20 );
			int sequence = map.ReadSpan( row + (split ? 0x31 : 0x30), 1 )[0];
			int sequences = Address( bitmap ) + 60;
			int count = map.ReadInt32( sequences );
			// HUD sequence zero addresses the first image when the bitmap has no sequences.
			int index = 0;
			if ( count == 0 )
			{
				if ( sequence != 0 || map.ReadInt32( sequences + 8 ) <= 0 ) return null;
			}
			else
			{
				if ( sequence >= count ) return null;
				int selected = Block( sequences ) + sequence * 60;
				index = Short( selected + 32 );
				if ( elite && Short( selected + 34 ) > 1 ) index++;
			}
			if ( meterFill is float fill )
			{
				int shader = Address( Reference( row + 0x28 ) );
				int postprocess = Block( shader + 0x20 );
				if ( map.ReadInt32( postprocess + 60 ) == 0 ) return null;
				int rgb = HudColor( postprocess, 0, elite );
				return new Halo2TextureLoader( map, bitmap, index, meter: (fill, rgb) );
			}
			if ( widget is "weapon_background_single" or "weapon_background_out" )
			{
				int shader = Address( Reference( row + 0x28 ) );
				int postprocess = Block( shader + 0x20 );
				if ( map.ReadInt32( postprocess + 60 ) < 5 ) return null;
				int ColorAt( int slot ) => HudColor( postprocess, slot, elite );
				return new Halo2TextureLoader( map, bitmap, index, hudGradient: (ColorAt( 1 ), ColorAt( 0 ), ColorAt( 4 )) );
			}
			return new Halo2TextureLoader( map, bitmap, index, hudMask: widget is "crosshair" or "crosshair_weakspot" );
		}
		return null;
	}

	Dictionary<string, TagEntry> casingMaterials, pelletMaterials, pelletEffects;

	void LoadMaterialEffects()
	{
		if ( pelletEffects is not null ) return;
		var globals = map.Tags.FirstOrDefault( t => t.PrimaryClass == "gtam" );
		if ( globals is null ) return;
		int materials = Address( globals ) + 0x150;
		int count = map.ReadInt32( materials ), rows = Block( materials );
		var casing = map.Tags.FirstOrDefault( t => t.TagPath == @"effects\objects\weapons\rifle\shotgun\shotgun_casing" );
		var projectile = Reference( Block( Header + 0x2d0 ) + 0x8c );
		casingMaterials = ReadBank( casing, 0x6c, 10, 8 );
		pelletMaterials = ReadBank( projectile, 0x54, 15, 8 );
		pelletEffects = ReadBank( projectile, 0x54, 15, 16 );

		Dictionary<string, TagEntry> ReadBank( TagEntry source, int referenceOffset, int action, int bankOffset )
		{
			var result = new Dictionary<string, TagEntry>( StringComparer.OrdinalIgnoreCase );
			if ( source is null || Reference( Address( source ) + referenceOffset ) is not { PrimaryClass: "toof" } foot ) return result;
			int actions = Address( foot );
			if ( map.ReadInt32( actions ) <= action ) return result;
			int bank = Block( actions ) + action * 24 + bankOffset;
			int entries = map.ReadInt32( bank );
			if ( entries == 0 ) return result;
			var contacts = new Dictionary<int, TagEntry>();
			int entry = Block( bank );
			for ( int i = 0; i < entries; i++, entry += 24 ) contacts[Short( entry + 20 )] = Reference( entry );
			for ( int i = 0; i < count; i++ )
			{
				if ( map.ResolveStringId( map.ReadInt32( rows + i * 0xb4 ) ) is not {} name ) continue;
				int index = i;
				for ( int depth = 0; (uint)index < count && depth < count; depth++ )
				{
					if ( contacts.TryGetValue( index, out var sound ) )
					{
						result[name] = sound;
						break;
					}
					index = Short( rows + index * 0xb4 + 8 );
				}
			}
			return result;
		}
	}

	ShotgunContacts ReadContacts()
	{
		LoadMaterialEffects();
		var cues = new List<CuePlayback>();
		var indices = new Dictionary<TagEntry, int>();
		int CueIndex( TagEntry tag )
		{
			if ( tag?.PrimaryClass != "!dns" ) return 0;
			if ( indices.TryGetValue( tag, out int index ) ) return index;
			if ( Host.GetByPath( Host.AssetPath( map, tag.TagPath, "sound" ) ) is not Halo2SoundLoader sound ) return 0;
			cues.Add( sound.Cue );
			return indices[tag] = cues.Count;
		}

		Dictionary<string, int> Bank( Dictionary<string, TagEntry> materials ) =>
			materials?.ToDictionary( entry => entry.Key, entry => CueIndex( entry.Value ), StringComparer.OrdinalIgnoreCase ) ?? new();

		return new ShotgunContacts
		{
			Pellets = Bank( pelletMaterials ),
			Effects = pelletEffects?.ToDictionary( e => e.Key, e => e.Value is null ? null : $"mount://{Host.Ident}/{ImpactPath( e.Value )}" ),
			Casings = Bank( casingMaterials ),
			Cues = cues.ToArray()
		};
	}


	PrefabFile prefab;

	protected override object Load()
	{
		var world = WorldModelPath;
		var firstPerson = Reference( Block( Header + 0x2a8 ) );
		int magazine = Block( Header + 0x2c0 );
		int barrel = Block( Header + 0x2d0 );
		int projectile = Address( Reference( barrel + 0x8c ) );
		int damage = Address( Reference( projectile + 0x148 ) );
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", "Shotgun" ).WithMetadata( "PreviewModel", world );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, "Shotgun" );
			var gun = ConfigurePickup<Shotgun>( root, world, "Shotgun" );
			gun.SpartanAmmoText = ReserveColors( false );
			gun.EliteAmmoText = ReserveColors( true );
			gun.BindHudTextures();
			gun.Contacts = ReadContacts();
			gun.HoldType = WeaponHolds.Shotgun;
			gun.ViewModelPath = Host.AssetPath( map, firstPerson.TagPath, "vmdl" );
			gun.ArmsPath = $"mount://{Host.Ident}/{RigPath}";
			(gun.ViewSmokeRadius, gun.ViewSmokeAlpha) = SmokeCurves( true );
			(gun.WorldSmokeRadius, gun.WorldSmokeAlpha) = SmokeCurves( false );
			gun.ViewOffset = new Vector3( Float( Header + 0x308 ), Float( Header + 0x30c ), Float( Header + 0x310 ) ) * 120;
			float acceleration = Float( Header + 0x2e8 ), speed = Float( Header + 0x2ec );
			if ( acceleration != 0 ) gun.TurnAcceleration = acceleration;
			if ( speed != 0 ) gun.TurnSpeed = speed;
			float moveAcceleration = Float( Header + 0x2e0 ), moveSpeed = Float( Header + 0x2e4 );
			if ( moveAcceleration != 0 ) gun.MovementAcceleration = moveAcceleration;
			if ( moveSpeed != 0 ) gun.MovementSpeed = moveSpeed;
			gun.PrimaryAmmoType = LoadShells();
			gun.ClipMaxSize = Short( magazine + 0xa );
			gun.StartingAmmo = Math.Max( 0, Short( magazine + 6 ) - gun.ClipMaxSize );
			gun.PrimaryAutomatic = Short( Block( Header + 0x2c8 ) + 6 ) == 0;
			gun.BarrelRecoveryTime = Float( barrel + 0x20 );
			// The barrel enters recovery one tick after firing and accepts the next shot one tick after recovery ends.
			gun.PrimaryDelay = Weapon.CooldownSeconds( (int)MathF.Floor( gun.BarrelRecoveryTime * 30 ) + 2 );
			var rig = (Halo2ModelLoader)Host.GetByPath( gun.ArmsPath );
			var actions = rig.GetAnimationContext( "any", "any", "any" ).Actions;
			var worldRig = (Halo2ModelLoader)Host.GetByPath( world );
			var chamber = worldRig.GetAnimationContext( "any", "any", "any" ).Actions["chamber_1"];
			gun.ChamberDuration = worldRig.GetClipInfo( chamber ).Frames / 30f;
			gun.ReloadTime = rig.GetClipInfo( actions["reload_continue_full"] ).Frames / 30f;
			gun.ReloadStartTime = rig.GetClipInfo( actions["reload_enter"] ).Frames / 30f + gun.ReloadTime;
			ConfigureKick( gun, barrel );
			gun.IncrementalReloading = Short( magazine + 0x14 ) == 1;
			gun.CanCancelReload = true;
			gun.DeployTime = Float( Header + 0x13c );
			var ballistics = gun.Ballistics;
			ballistics.Pellets = Short( barrel + 0x6a );
			ballistics.Range = Float( projectile + 0xe0 ) * 120;
			gun.AirGravityScale = Float( projectile + 0x164 );
			gun.WaterGravityScale = Float( projectile + 0x170 );
			gun.PelletSpeed = Float( projectile + 0x17c ) * 120;
			gun.SpeedProfile = ReadSpeedProfile( projectile );
			gun.AutoAimRange = Float( Header + 0x20c ) * 120;
			gun.AutoAimAngle = Float( Header + 0x208 );
			gun.MagnetismAngle = Float( Header + 0x210 );
			gun.MagnetismRange = Float( Header + 0x214 ) * 120;
			bool damageFallsOff = (map.ReadInt32( projectile + 0xbc ) & 16) != 0;
			gun.DamageFalloffRange = damageFallsOff ? Float( projectile + 0x16c ) * 120 : 0;
			gun.UnderwaterDamageFalloffRange = damageFallsOff ? Float( projectile + 0x178 ) * 120 : 0;
			gun.MinimumDamage = Float( damage + 0x1c );
			ballistics.Damage = Float( damage + 0x20 );
			gun.MinimumPelletAngle = Float( barrel + 0x70 ).RadianToDegree();
			ballistics.SpreadBase = Vector2.One * Float( barrel + 0x74 ).RadianToDegree();
			ballistics.SpreadGrowth = Vector2.One * (Float( barrel + 0x78 ) - Float( barrel + 0x74 )).RadianToDegree();
			ballistics.SpreadRecovery = gun.BarrelRecoveryTime;
			ballistics.Force = Float( damage + 0x40 ) * 120;
			ballistics.Radius = 0;
			gun.Ballistics = ballistics;
			gun.AttackSound = FiringSound( barrel, out var marker );
			gun.AttackSoundMarker = marker;
			gun.DryFireSound = FiringSound( barrel, out var dryMarker, dry: true );
			gun.DryFireMarker = dryMarker;
			var globals = map.Tags.First( t => t.PrimaryClass == "gtam" );
			int playerControl = Block( Address( globals ) + 0xf0 );
			gun.MagnetismFriction = Float( playerControl );
			gun.MagnetismAdhesion = Float( playerControl + 4 );
			gun.InconsequentialTargetScale = Float( playerControl + 8 );
			gun.PelletBodyScales = BodyDamageScales( Address( globals ), damage );
			gun.PelletExplosive = (map.ReadInt32( damage + 0x14 ) & 0x20) != 0;
			gun.VerticalAimOffset = Float( Block( Address( globals ) + 0xf0 ) + 0x1c );
		}
		return prefab = builder.Create();
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		base.Shutdown();
	}
}
