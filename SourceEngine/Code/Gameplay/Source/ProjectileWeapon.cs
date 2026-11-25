using System;

namespace Source1;

/// <summary>Shared physical projectile weapon; the catalog supplies ballistics and mounted art.</summary>
[Title( "Source Projectile Weapon" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceProjectileWeapon", "SourceProjectileWeapon" )]
public sealed class ProjectileWeapon : Weapon
{
	public ProjectileWeapon()
	{
		// Not a gun: the projectile it launches does the damage.
		AttackDamageTags = [];
	}

	[Property] public Projectile.Kinds Kind { get; set; }
	[Property] public string ProjectileModel { get; set; }
	[Property] public float ProjectileDamage { get; set; } = 100;
	[Property] public float ExplosionRadius { get; set; } = 250;
	[Property] public SecondaryFires Secondary { get; set; }
	/// <summary>Time from firing the last round to starting an automatic magazine reload.</summary>
	[Property] public float AutoReloadDelay { get; set; }
	[Sync( SyncFlags.FromHost )] public GameObject LiveRocket { get; private set; }
	/// <summary>HL2's launcher guides by default; the SDK's multiplayer rules toggle it with attack2.</summary>
	[Sync( SyncFlags.FromHost )] public bool Guiding { get; private set; }
	internal float WindupDuration { get; set; }
	internal float ReleaseDelay { get; set; }
	internal float AttackDuration { get; set; }
	internal float RedrawDuration { get; set; }
	internal float? BoltChargeDelay { get; set; }
	TimeUntil? _boltChargeAt;
	internal float? NpcDamage { get; set; }
	bool _primed;
	bool _releasePending;
	GameObject _releaseAttacker;
	bool _redrawPending;
	TimeUntil _releaseAt;
	TimeUntil _redrawAt;
	Zoom _zoom;
	Hl2.RpgLaser _laser;

	bool IsLauncher => Kind == Projectile.Kinds.Rocket;
	bool LaserOn => IsLauncher && Guiding && Active && !IsReloading;

	/// <summary>Where the laser dot sits; the live rocket steers toward it.</summary>
	public Vector3? TraceLaserTarget() => LaserOn ? Scene.Trace.Ray( AimRay, 16384 ).IgnoreGameObjectHierarchy( AimIgnoreRoot )
		.WithoutTags( "projectile", "trigger" ).Run().EndPosition : null;

	protected override void OnStart()
	{
		base.OnStart();
		_ = WeaponSetup.ConfigureWeapon( this );
	}

	public override bool CanPrimaryAttack() => !_primed && !_releasePending && !_redrawPending && !LiveRocket.IsValid() && base.CanPrimaryAttack();
	// CWeaponRPG reloads only once its missile is gone (NotifyRocketDied).
	public override bool CanReload() => !_primed && !_releasePending && !_redrawPending && !LiveRocket.IsValid() && base.CanReload();
	protected override bool WantsReload() => base.WantsReload() || (AutoReload && AutoReloadDelay > 0
		&& Clip1 == 0 && TimeSinceShoot >= AutoReloadDelay && Ammo1 > 0);

	public override bool CanSecondaryAttack() => Secondary == SecondaryFires.Zoom || (Secondary == SecondaryFires.Guide && base.CanSecondaryAttack());

	public override void SecondaryAttack()
	{
		if ( Secondary == SecondaryFires.Zoom )
		{
			if ( HasOwner && !IsProxy && Scene.Camera.IsValid() )
			{
				_zoom = Scene.Camera.GetOrAddComponent<Zoom>();
				_zoom.SetZoomed( !_zoom.Zoomed );
			}
			return;
		}
		SetNextSecondaryFire( 0.3f );
		if ( Secondary == SecondaryFires.Guide ) ToggleGuiding();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void ToggleGuiding() => SetGuiding( !Guiding );

	void SetGuiding( bool on )
	{
		if ( Guiding == on ) return;
		Guiding = on;
		GuidingSound( on );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void GuidingSound( bool on )
	{
		if ( Sounds.WeaponSound( this, on ? "special1" : "special2" ) is { } sound ) GameObject.PlaySound( sound );
	}

	/// <summary>Host only: CWeaponRPG::NotifyRocketDied reloads the launcher.</summary>
	internal void NotifyRocketDied()
	{
		LiveRocket = null;
		if ( !IsValid ) return;
		if ( !IsHeld || !Active || !AutoReload ) return;
		if ( UsesPrimaryClip )
		{
			if ( CanReload() ) Reload();
		}
		else if ( HasPrimaryAmmo() ) ReloadLauncher();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ReloadLauncher()
	{
		SetNextFire( ReloadTime );
		_redrawPending = true;
		_redrawAt = ReloadTime;
		OnReloadStarted();
	}

	void ReleaseZoom()
	{
		if ( _zoom.IsValid() ) _zoom.SetZoomed( false );
		_zoom = null;
	}
	protected override void OnReloadStarted()
	{
		base.OnReloadStarted();
		if ( !Application.IsDedicatedServer && BoltChargeDelay is { } delay ) _boltChargeAt = (TimeUntil)delay;
		if ( HasOwner && !Owner.IsProxy && WeaponModel is not Source1.ViewModel ) Sounds.PlayReloadSound( this, GameObject );
	}

	protected override void OnReloadFinished()
	{
		_boltChargeAt = null;
		base.OnReloadFinished();
	}

	protected override void OnShootEffects( ShotEffect shot )
	{
		base.OnShootEffects( shot );
		if ( Kind != Projectile.Kinds.Bolt || shot.NoEvents || Application.IsDedicatedServer ) return;
		WeaponEffects.CrossbowLoad( WeaponModel, MountContent.OfWeapon( this ) );
		if ( Sounds.WeaponSound( this, "special2" ) is { } flight ) GameObject.PlaySound( flight );
	}

	public override void DrawCrosshair( Painter painter, Vector2 center ) => Crosshair.Draw( painter, center, CanPrimaryAttack(), SpreadBloom );

	protected override bool OnHolstering( BaseInventoryItem next ) => !LiveRocket.IsValid() && base.OnHolstering( next );

	protected override void OnEquipped()
	{
		if ( Networking.IsHost && IsLauncher && Secondary == SecondaryFires.Guide ) Guiding = true;
		base.OnEquipped();
	}

	protected override void OnHolstered()
	{
		if ( Networking.IsHost && IsLauncher ) SetGuiding( false );
		ReleaseZoom();
		_primed = false;
		_releasePending = false;
		_releaseAttacker = null;
		_redrawPending = false;
		base.OnHolstered();
	}

	protected override void OnDisabled()
	{
		ReleaseZoom();
		_laser?.Dispose();
		_laser = null;
		base.OnDisabled();
	}

	public override void PrimaryAttack()
	{
		if ( _primed || _releasePending || _redrawPending ) return;
		if ( AttackDuration > 0 )
		{
			_releaseAttacker = Attacker;
			ShootEffects();
			if ( WindupDuration > 0 )
			{
				_primed = true;
				_releaseAt = WindupDuration;
			}
			else BeginThrow();
			return;
		}
		if ( UsesAmmo && HasOwner && !TakePrimaryAmmo( 1 ) )
		{
			DryFire();
			return;
		}
		TimeSinceShoot = 0;
		SetNextFire( PrimaryDelay );
		ShootEffects();
		KickView();
		Launch( AimRay.Position, AimRay.Forward, Attacker );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( _boltChargeAt is { } charge && charge <= 0 )
		{
			_boltChargeAt = null;
			if ( IsReloading )
			{
				WeaponEffects.CrossbowLoad( WeaponModel, MountContent.OfWeapon( this ) );
				if ( Sounds.WeaponSound( this, "special1" ) is { } sound ) GameObject.PlaySound( sound );
			}
		}
		if ( !IsLauncher || Application.IsDedicatedServer ) return;
		_laser ??= new Hl2.RpgLaser();
		_laser.Update( this, LaserOn );
	}

	protected override void OnViewModelPlaced( ViewModel model )
	{
		_laser?.Follow( model );
		if ( Kind != Projectile.Kinds.Bolt || !model.Renderer.IsValid() ) return;

		var charged = Clip1 > 0 || (IsReloading && BoltChargeDelay.HasValue && _boltChargeAt is null);
		var skin = charged ? "Skin 1" : "Skin 0";
		if ( model.Renderer.MaterialGroup != skin && model.Renderer.Model is { MaterialGroupCount: >= 2 } ) model.Renderer.MaterialGroup = skin;
	}

	protected override void Think()
	{
		base.Think();
		if ( _primed && _releaseAt <= 0 && !Input.Down( "attack1" ) ) BeginThrow();
		if ( IsLauncher && _redrawPending && _redrawAt <= 0 && !AttackOrReloadHeld ) FinishRedraw();
	}

	void BeginThrow()
	{
		if ( _primed ) ThrowEffects();
		_primed = false;
		_releasePending = true;
		_releaseAt = ReleaseDelay;
		_redrawAt = AttackDuration;
		SetNextFire( Math.Max( PrimaryDelay, AttackDuration + RedrawDuration ) );
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void ThrowEffects() => (WeaponModel as ViewModel)?.OnThrow();

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( Networking.IsHost && IsLauncher && Active && Secondary != SecondaryFires.Guide )
			SetGuiding( !HasOwner || (!IsDeploying && (HasPrimaryAmmo() || LiveRocket.IsValid())) );
		if ( _primed && !HasOwner && _releaseAt <= 0 ) BeginThrow();
		if ( _releasePending && _releaseAt <= 0 )
		{
			var attacker = _releaseAttacker;
			_releasePending = false;
			_releaseAttacker = null;
			if ( UsesAmmo && IsHeld && !TakePrimaryAmmo( 1 ) ) return;
			TimeSinceShoot = 0;
			KickView();
			Launch( AimRay.Position, AimRay.Forward, attacker );
			_redrawPending = true;
		}
		if ( _redrawPending && _redrawAt <= 0 && (!IsLauncher || !HasOwner || IsProxy) ) FinishRedraw();
	}

	void FinishRedraw()
	{
		_redrawPending = false;
		if ( IsLauncher && !UsesPrimaryClip ) OnReloadFinished();
		else if ( IsHeld && AutoReload && CanReload() ) Reload();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Launch( Vector3 eye, Vector3 forward, GameObject attacker )
	{
		var damage = GetComponentInParent<Sandbox.Npcs.BaseNpc>().IsValid() ? NpcDamage ?? ProjectileDamage : ProjectileDamage;
		var inherited = Owner?.Velocity ?? Vector3.Zero;
		var (origin, velocity) = Kind switch
		{
			Projectile.Kinds.Grenade => (ThrowOrigin( eye, forward, 8 ), Hl2.Grenade.ThrowVelocity( forward, inherited )),
			Projectile.Kinds.Bugbait => (ThrowOrigin( eye, forward, 12 ), Hl2.Bugbait.ThrowVelocity( forward, inherited )),
			Projectile.Kinds.HeGrenade or Projectile.Kinds.Flashbang or Projectile.Kinds.SmokeGrenade => (ThrowOrigin( eye, forward, 0 ), Css.Rules.ThrowVelocity( forward, inherited )),
			Projectile.Kinds.Rocket => (eye + Rotation.LookAt( forward ) * new Vector3( 12, -6, -3 ), Hl2.Rocket.LaunchVelocity( forward )),
			_ => (eye, Hl2.CrossbowBolt.LaunchVelocity( Scene, eye, forward ))
		};
		var radius = IsLauncher ? damage * 2 : ExplosionRadius;
		var projectile = Projectile.Launch( this, ResolveLaunchAttacker( attacker ), Kind, origin, velocity, damage, radius, ProjectileModel );
		if ( projectile is Hl2.Rocket rocket )
		{
			rocket.ClearLaunch = !Scene.Trace.Ray( eye, eye + forward * 128 )
				.IgnoreGameObjectHierarchy( AimIgnoreRoot ).WithoutTags( "projectile", "trigger" ).UseHitboxes().Run().Hit;
			LiveRocket = rocket.GameObject;
		}
	}

	/// <summary>
	/// weapon_frag and weapon_bugbait release 18 units ahead of the eye and a little to the right,
	/// pulled back along the line when that would start inside a wall (CheckThrowPosition).
	/// </summary>
	Vector3 ThrowOrigin( Vector3 eye, Vector3 forward, float right )
	{
		var target = eye + forward * 18 + Rotation.LookAt( forward ).Right * right;
		var tr = Scene.Trace.Box( BBox.FromPositionAndSize( Vector3.Zero, 12 ), eye, target )
			.IgnoreGameObjectHierarchy( AimIgnoreRoot ).WithoutTags( "trigger", "ragdoll", "projectile" ).Run();
		return tr.Hit ? tr.EndPosition : target;
	}
}
