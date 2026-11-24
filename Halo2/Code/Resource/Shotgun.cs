using Halo2.Blam;
using Sandbox.Mounting;
using System;

namespace Halo2;

internal sealed class ShotgunAmmoLoader( int capacity ) : ResourceLoader<Halo2Mount>
{
	protected override object Load() => new BaseAmmoResource { Title = "Shotgun shells", MaxReserve = capacity };
}

internal sealed partial class ShotgunLoader( MapFile map, TagEntry weapon ) : ResourceLoader<Halo2Mount>
{
	internal const string PrefabPath = "weapons/shotgun.prefab";
	internal static ShotgunLoader Mounted => Directory.Get( Halo2Mount.MountIdent )?.GetByPath( $"mount://{Halo2Mount.MountIdent}/{PrefabPath}" ) as ShotgunLoader;
	internal const string AmmoPath = "ammo/shotgun_shells.ammo";
	int Header => map.VaToOffset( (int)weapon.DataVirtualAddress );
	int Block( int offset ) => map.VaToOffset( map.ReadInt32( offset + 4 ) );
	int Short( int offset ) => (short)map.ReadInt32( offset );
	float Float( int offset ) => BitConverter.Int32BitsToSingle( map.ReadInt32( offset ) );
	TagEntry Reference( int offset ) => map.GetTag( map.ReadInt32( offset + 4 ) & 0xffff );
	int Address( TagEntry tag ) => map.VaToOffset( (int)tag.DataVirtualAddress );
	internal const string RigPath = "weapons/shotgun/first_person.vmdl";
	internal Halo2ModelLoader CreateRig()
	{
		// Campaign Spartan representation supplies both the hands and their object change colors.
		var globals = map.Tags.First( t => t.PrimaryClass == "gtam" );
		int representation = Block( Address( globals ) + 0x138 );
		int row = Block( Header + 0x2a8 );
		return new Halo2ModelLoader( map, Reference( representation ), biped: Reference( representation + 0xb0 ), graph: Reference( row + 8 ), firstPersonWeapon: Reference( row ) );
	}
	internal const string ReticlePath = "weapons/shotgun/reticle.vtex";
	internal const string IconPath = "weapons/shotgun/icon.vtex";
	internal const string SplitReticleName = "split_reticle.vtex";
	internal const string SplitWeakReticleName = "split_weak_reticle.vtex";
	internal const string WeakReticleName = "weak_reticle.vtex";
	internal const string MuzzleFlashName = "muzzle_flash.vtex";
	internal const string LongFlashName = "muzzle_flash_long.vtex";
	internal const string CasingName = "casing.vtex";
	internal const string SmokeName = "smoke_soft.vtex";
	internal const string FierySmokeName = "smoke_fiery.vtex";
	internal const string SparksName = "sparks_animated.vtex";

	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = ResourceType.Texture;
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

		return Halo2TextureLoader.SpriteSheet( map, bitmap, (flags & 8) == 0 );
	}

	internal (CurveRange Radius, CurveRange Alpha) SmokeCurves( bool firstPerson )
	{
		int barrel = Block( Header + 0x2d0 );
		int systems = Block( FiringEvent( barrel ) + 48 );
		int emitter = Block( systems + 56 * (firstPerson ? 6 : 2) + 48 );
		return (ReadRange( emitter + 72, 120 ), ReadRange( emitter + 104, 1 ));

		CurveRange ReadRange( int property, float units )
		{
			int data = Block( property + 8 );
			float min = Float( data + 4 ) * units, span = Float( data + 8 ) * units - min;
			// A ranged cubic stores two 48-byte polynomials. Convert their endpoint slopes to Hermite keys.
			return new CurveRange( ReadCurve( data + 52 ), ReadCurve( data + 100 ) );

			Curve ReadCurve( int offset )
			{
				float a = Float( offset ), b = Float( offset + 4 ), c = Float( offset + 8 ), d = Float( offset + 12 );
				return new Curve( new Curve.Frame( 0, min + span * d, 0, span * c ),
					new Curve.Frame( 1, min + span * (a + b + c + d), -span * (3 * a + 2 * b + c), 0 ) );
			}
		}
	}

	int FiringEvent( int barrel ) => Block( Address( Reference( Block( barrel + 0xe4 ) + 4 ) ) + 20 );

	SoundEvent FiringSound( int barrel )
	{
		int parts = FiringEvent( barrel ) + 0x18;
		int count = map.ReadInt32( parts );
		if ( count == 0 ) return null;
		int first = Block( parts );
		for ( int i = 0; i < count; i++ )
		{
			var tag = Reference( first + i * 0x38 + 0xc );
			if ( tag?.PrimaryClass == "!dns" && tag.TagPath.StartsWith( @"sound\", StringComparison.OrdinalIgnoreCase ) )
				return GameResource.Load<SoundEvent>( Host.AssetPath( map, tag.TagPath, "sound" ) );
		}
		return null;
	}

	internal Halo2TextureLoader HudBitmap( string widget, bool split = false )
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
			if ( sequence >= map.ReadInt32( sequences ) ) return null;
			int index = Short( Block( sequences ) + sequence * 60 + 32 );
			return new Halo2TextureLoader( map, bitmap, index, hudMask: widget is "crosshair" or "crosshair_weakspot" );
		}
		return null;
	}

	static string MaterialName( Surface surface ) => surface.ResourceName switch
	{
		"default" => "default_material", "flesh" => "soft_organic_flesh", "glass" => "brittle_glass",
		"concrete" => "hard_terrain_concrete", "rock" or "stone" => "hard_terrain_stone", "ice" => "hard_terrain_ice",
		"wood" or "wood.sheet" => "tough_organic_wood", "metal" => "hard_metal_solid", "metal.sheet" => "hard_metal_thin",
		"rubber" => "tough_inorganic_rubber", "plastic" or "plastic.sheet" => "tough_inorganic_plastic",
		"cloth" or "fabric" => "soft_organic_cloth", "water" => "liquid_thin_water", "grass" => "tough_terrain_grass",
		"dirt" => "tough_terrain_dirt", "sand" => "tough_terrain_sand", "gravel" => "tough_terrain_gravel",
		"mud" => "tough_terrain_mud", "snow" => "soft_terrain_snow",
		_ => surface.AudioSurface switch
		{
			AudioSurface.Concrete or AudioSurface.Brick => "hard_terrain_concrete",
			AudioSurface.Rock or AudioSurface.Marble => "hard_terrain_stone",
			AudioSurface.Metal or AudioSurface.Steel => "hard_metal_solid",
			AudioSurface.Wood => "tough_organic_wood",
			AudioSurface.Glass => "brittle_glass",
			AudioSurface.Gravel => "tough_terrain_gravel",
			AudioSurface.Soil => "tough_terrain_dirt",
			AudioSurface.Sand => "tough_terrain_sand",
			AudioSurface.Snow => "soft_terrain_snow",
			AudioSurface.Fabric or AudioSurface.Curtain => "soft_organic_cloth",
			AudioSurface.Water => "liquid_thin_water",
			_ => null
		}
	};

	static TagEntry ContactSound( Dictionary<string, TagEntry> sounds, Surface surface, bool useDefault )
	{
		for ( int depth = 0; surface.IsValid() && depth < 16; depth++, surface = surface.GetBaseSurface() )
		{
			if ( surface.ResourceName is {} name && sounds.TryGetValue( name, out var sound ) ) return sound;
			if ( MaterialName( surface ) is {} material && sounds.TryGetValue( material, out sound ) ) return sound;
		}
		return useDefault ? sounds.GetValueOrDefault( "default_material" ) : null;
	}

	Dictionary<string, TagEntry> meleeMaterials, casingMaterials, pelletMaterials;
	uint impactSoundSeed = (uint)System.Random.Shared.Next();

	void LoadImpactSounds()
	{
		if ( meleeMaterials is not null ) return;
		meleeMaterials = new( StringComparer.OrdinalIgnoreCase );
		casingMaterials = new( StringComparer.OrdinalIgnoreCase );
		var globals = map.Tags.FirstOrDefault( t => t.PrimaryClass == "gtam" );
		if ( globals is null ) return;
		int materials = Address( globals ) + 0x150;
		int count = map.ReadInt32( materials ), rows = Block( materials );
		var casing = map.Tags.FirstOrDefault( t => t.TagPath == @"effects\objects\weapons\rifle\shotgun\shotgun_casing" );
		var projectile = Reference( Block( Header + 0x2d0 ) + 0x8c );
		casingMaterials = ReadBank( casing, 0x6c, 10 );
		pelletMaterials = ReadBank( projectile, 0x54, 15 );
		for ( int i = 0; i < count; i++ )
		{
			int row = rows + i * 0xb4;
			if ( map.ResolveStringId( map.ReadInt32( row ) ) is {} name ) meleeMaterials[name] = Reference( row + 0x60 );
		}

		Dictionary<string, TagEntry> ReadBank( TagEntry source, int referenceOffset, int action )
		{
			var result = new Dictionary<string, TagEntry>( StringComparer.OrdinalIgnoreCase );
			if ( source is null || Reference( Address( source ) + referenceOffset ) is not { PrimaryClass: "toof" } foot ) return result;
			int actions = Address( foot );
			if ( map.ReadInt32( actions ) <= action ) return result;
			int bank = Block( actions ) + action * 24 + 8;
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

	internal SoundHandle PlayPelletImpact( Surface surface, Vector3 position )
	{
		LoadImpactSounds();
		var sound = pelletMaterials is null ? null : ContactSound( pelletMaterials, surface, false );
		return PlayImpact( sound, position );
	}

	SoundHandle PlayImpact( TagEntry sound, Vector3 position ) =>
		sound is not null && Host.GetByPath( Host.AssetPath( map, sound.TagPath, "sound" ) ) is Halo2SoundLoader loader
			? loader.PlayCue( position, ref impactSoundSeed ) : null;

	internal SoundHandle PlayCasingImpact( Surface surface, Vector3 position, float gain )
	{
		if ( gain <= 0 ) return null;
		LoadImpactSounds();
		var sound = ContactSound( casingMaterials, surface, true );
		var handle = PlayImpact( sound, position );
		if ( handle.IsValid() ) handle.Volume *= gain;
		return handle;
	}

	internal void PlayMeleeImpact( Surface surface, Vector3 position )
	{
		LoadImpactSounds();
		Play( ContactSound( meleeMaterials, surface, false ) );

		if ( Reference( Header + 0x1bc ) is {} damage ) Play( Reference( Address( damage ) + 0xa8 ) );

		void Play( TagEntry tag )
		{
			if ( tag?.PrimaryClass == "!dns" && GameResource.Load<SoundEvent>( Host.AssetPath( map, tag.TagPath, "sound" ) ) is {} sound )
				Sound.Play( sound, position );
		}
	}

	internal int MaxReserve => Short( Block( Header + 0x2c0 ) + 0xc );
	PrefabFile prefab;

	protected override object Load()
	{
		var modelTag = Reference( Address( Reference( Header + 0x34 ) ) );
		var world = Host.AssetPath( map, modelTag.TagPath, "vmdl" );
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
			root.NetworkMode = NetworkMode.Object;
			root.Tags.Add( "removable", "nocollide_props" );
			var renderer = root.AddComponent<SkinnedModelRenderer>();
			renderer.Model = Model.Load( world );
			var collider = root.AddComponent<BoxCollider>();
			collider.Center = renderer.Model.Bounds.Center;
			collider.Scale = renderer.Model.Bounds.Size;
			root.AddComponent<Rigidbody>();
			var gun = root.AddComponent<Shotgun>();
			gun.DisplayName = "Shotgun";
			gun.BindHudTextures();
			gun.HoldType = WeaponHolds.Shotgun;
			gun.ViewModelPath = Host.AssetPath( map, firstPerson.TagPath, "vmdl" );
			gun.ArmsPath = $"mount://{Host.Ident}/{RigPath}";
			gun.ViewOffset = new Vector3( Float( Header + 0x308 ), Float( Header + 0x30c ), Float( Header + 0x310 ) ) * 120;
			float acceleration = Float( Header + 0x2e8 ), speed = Float( Header + 0x2ec );
			if ( acceleration != 0 ) gun.TurnAcceleration = acceleration;
			if ( speed != 0 ) gun.TurnSpeed = speed;
			float moveAcceleration = Float( Header + 0x2e0 ), moveSpeed = Float( Header + 0x2e4 );
			if ( moveAcceleration != 0 ) gun.MovementAcceleration = moveAcceleration;
			if ( moveSpeed != 0 ) gun.MovementSpeed = moveSpeed;
			gun.PrimaryAmmoType = GameResource.Load<BaseAmmoResource>( $"mount://{Host.Ident}/{AmmoPath}" );
			gun.ClipMaxSize = Short( magazine + 0xa );
			gun.StartingAmmo = Math.Max( 0, Short( magazine + 6 ) - gun.ClipMaxSize );
			gun.PrimaryAutomatic = Short( Block( Header + 0x2c8 ) + 6 ) == 0;
			gun.PrimaryDelay = Float( barrel + 0x20 );
			var rig = (Halo2ModelLoader)Host.GetByPath( gun.ArmsPath );
			var actions = rig.GetAnimationContext( "any", "any", "any" ).Actions;
			gun.ReloadTime = rig.GetClipInfo( actions["reload_continue_full"] ).Frames / 30f;
			gun.ReloadStartTime = rig.GetClipInfo( actions["reload_enter"] ).Frames / 30f + gun.ReloadTime;
			int trigger = Address( Reference( Block( barrel + 0xe4 ) + 0x1c ) );
			gun.KickDuration = Float( trigger + 0x74 );
			gun.KickDegrees = Float( trigger + 0x7c ).RadianToDegree();
			gun.KickDistance = Float( trigger + 0x80 ) * 120;
			gun.IncrementalReloading = Short( magazine + 0x14 ) == 1;
			gun.CanCancelReload = true;
			gun.DeployTime = Float( Header + 0x13c );
			var ballistics = gun.Ballistics;
			ballistics.Pellets = Short( barrel + 0x6a );
			ballistics.Range = Float( projectile + 0xe0 ) * 120;
			gun.AirGravityScale = Float( projectile + 0x164 );
			gun.WaterGravityScale = Float( projectile + 0x170 );
			gun.PelletSpeed = Float( projectile + 0x17c ) * 120;
			gun.AutoAimRange = Float( Header + 0x20c ) * 120;
			bool damageFallsOff = (map.ReadInt32( projectile + 0xbc ) & 16) != 0;
			gun.DamageFalloffRange = damageFallsOff ? Float( projectile + 0x16c ) * 120 : 0;
			gun.UnderwaterDamageFalloffRange = damageFallsOff ? Float( projectile + 0x178 ) * 120 : 0;
			gun.MinimumDamage = Float( damage + 0x1c );
			ballistics.Damage = Float( damage + 0x20 );
			gun.MinimumPelletAngle = Float( barrel + 0x70 ).RadianToDegree();
			ballistics.SpreadBase = Vector2.One * Float( barrel + 0x74 ).RadianToDegree();
			ballistics.SpreadGrowth = Vector2.One * (Float( barrel + 0x78 ) - Float( barrel + 0x74 )).RadianToDegree();
			ballistics.SpreadRecovery = gun.PrimaryDelay;
			ballistics.Force = Float( damage + 0x40 ) * 120;
			ballistics.Radius = 0;
			gun.Ballistics = ballistics;
			gun.AttackSound = FiringSound( barrel );
			gun.MeleeHalfExtents = new Vector2( Float( Header + 0x1b0 ), Float( Header + 0x1b4 ) ) * 120;
			gun.MeleeRange = Float( Header + 0x1b8 ) * 120;
			int strike = Address( Reference( Header + 0x1bc ) );
			gun.MinimumMeleeDamage = Float( strike + 0x1c );
			gun.MeleeDamage = Float( strike + 0x20 );
			var globals = map.Tags.First( t => t.PrimaryClass == "gtam" );
			gun.MeleeForwardSpeed = Float( Block( Address( globals ) + 0x130 ) + 0x2c );
			gun.MeleeForce = Float( strike + 0x40 ) * 120;
		}
		return prefab = builder.Create();
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		base.Shutdown();
	}
}
