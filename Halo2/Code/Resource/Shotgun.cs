using Halo2.Blam;
using Sandbox.Mounting;
using System;

namespace Halo2;

internal sealed class ShotgunAmmoLoader( int capacity ) : ResourceLoader<Halo2Mount>
{
	protected override object Load() => new BaseAmmoResource { Title = "Shotgun shells", MaxReserve = capacity };
}

internal sealed class ShotgunLoader( MapFile map, TagEntry weapon ) : ResourceLoader<Halo2Mount>
{
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
		var arms = map.Tags.First( t => t.PrimaryClass == "edom" && t.TagPath == @"objects\characters\masterchief\fp\fp" );
		int row = Block( Header + 0x2a8 );
		return new Halo2ModelLoader( map, arms, graph: Reference( row + 8 ), firstPersonWeapon: Reference( row ) );
	}
	internal const string ReticlePath = "weapons/shotgun/reticle.vtex";
	internal const string IconPath = "weapons/shotgun/icon.vtex";

	internal Halo2TextureLoader HudBitmap( string widget )
	{
		int hud = Address( Reference( Header + 0x2b0 ) );
		int widgets = Block( hud + 8 );
		for ( int i = 0; i < map.ReadInt32( hud + 8 ); i++ )
		{
			int row = widgets + i * 100;
			if ( map.ResolveStringId( map.ReadInt32( row ) ) != widget ) continue;
			var bitmap = Reference( row + 0x20 );
			int sequence = map.ReadSpan( row + 0x30, 1 )[0];
			int sequences = Address( bitmap ) + 60;
			if ( sequence >= map.ReadInt32( sequences ) ) return null;
			int index = Short( Block( sequences ) + sequence * 60 + 32 );
			return new Halo2TextureLoader( map, bitmap, index, hudMask: widget == "crosshair" );
		}
		return null;
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
			var gun = root.AddComponent<Halo2Shotgun>();
			gun.DisplayName = "Shotgun";
			gun.BindHudTextures();
			gun.HoldType = WeaponHolds.Shotgun;
			gun.ViewModelPath = Host.AssetPath( map, firstPerson.TagPath, "vmdl" );
			gun.ArmsPath = $"mount://{Host.Ident}/{RigPath}";
			gun.ViewOffset = new Vector3( Float( Header + 0x308 ), Float( Header + 0x30c ), Float( Header + 0x310 ) ) * 120;
			float acceleration = Float( Header + 0x2e8 ), speed = Float( Header + 0x2ec );
			if ( acceleration != 0 ) gun.TurnAcceleration = acceleration;
			if ( speed != 0 ) gun.TurnSpeed = speed;
			gun.PrimaryAmmoType = GameResource.Load<BaseAmmoResource>( $"mount://{Host.Ident}/{AmmoPath}" );
			gun.ClipMaxSize = Short( magazine + 0xa );
			gun.StartingAmmo = Math.Max( 0, Short( magazine + 6 ) - gun.ClipMaxSize );
			gun.PrimaryAutomatic = Short( Block( Header + 0x2c8 ) + 6 ) == 0;
			gun.PrimaryDelay = Float( barrel + 0x20 );
			gun.ReloadTime = Float( magazine + 0x10 );
			var rig = (Halo2ModelLoader)Host.GetByPath( gun.ArmsPath );
			var actions = rig.GetAnimationContext( "any", "any", "any" ).Actions;
			gun.ReloadStartTime = (rig.GetClipInfo( actions["reload_enter"] ).Frames - 1) / 30f + gun.ReloadTime;
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
			gun.PelletSpeed = Float( projectile + 0x17c ) * 120;
			gun.FullDamageRange = Float( projectile + 0x168 ) * 120;
			gun.MinimumDamage = Float( damage + 0x1c );
			ballistics.Damage = Float( damage + 0x20 );
			ballistics.SpreadBase = Vector2.One * Float( barrel + 0x74 ).RadianToDegree();
			ballistics.SpreadGrowth = Vector2.One * (Float( barrel + 0x78 ) - Float( barrel + 0x74 )).RadianToDegree();
			ballistics.SpreadRecovery = gun.PrimaryDelay;
			ballistics.Force = Float( damage + 0x40 ) * 120;
			ballistics.Radius = 0;
			gun.Ballistics = ballistics;
			gun.AttackSound = GameResource.Load<SoundEvent>( Host.AssetPath( map, "sound/weapons/shotgun/fire", "sound" ) );
			gun.MeleeRange = Float( Header + 0x1b8 ) * 120;
			gun.MeleeDamage = Float( Address( Reference( Header + 0x1bc ) ) + 0x20 );
		}
		return prefab = builder.Create();
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		base.Shutdown();
	}
}
