using System;

namespace Source1;

/// <summary>Shared physical projectile weapon; the catalog supplies ballistics and mounted art.</summary>
[Title( "Source Projectile Weapon" )]
[Category( "Source 1" )]
public sealed class SourceProjectileWeapon : SourceWeapon
{
	public SourceProjectileWeapon()
	{
		// Not a gun: the projectile it launches does the damage.
		AttackDamageTags = [];
	}

	[Property] public SourceProjectile.Kinds Kind { get; set; }
	[Property] public string ProjectileModel { get; set; }
	[Property] public float ProjectileDamage { get; set; } = 100;
	[Property] public float ExplosionRadius { get; set; } = 250;
	[Property] public SecondaryFires Secondary { get; set; }
	[Sync( SyncFlags.FromHost )] public GameObject LiveRocket { get; private set; }
	/// <summary>HL2's launcher guides by default; the SDK's multiplayer rules toggle it with attack2.</summary>
	[Sync( SyncFlags.FromHost )] public bool Guiding { get; private set; } = true;
	internal float ReleaseDelay { get; set; }
	internal float AttackDuration { get; set; }
	internal float RedrawDuration { get; set; }
	internal float? NpcDamage { get; set; }
	bool _releasePending;
	GameObject _releaseAttacker;
	bool _redrawPending;
	TimeUntil _releaseAt;
	TimeUntil _redrawAt;
	bool _zoomed;
	Hl2RpgLaser _laser;

	bool IsLauncher => Kind == SourceProjectile.Kinds.Rocket;
	bool LaserOn => IsLauncher && Guiding && Active && !IsReloading;

	/// <summary>Where the laser dot sits; the live rocket steers toward it.</summary>
	public Vector3? TraceLaserTarget() => LaserOn ? Scene.Trace.Ray( AimRay, 16384 ).IgnoreGameObjectHierarchy( AimIgnoreRoot )
		.WithoutTags( "projectile", "trigger" ).Run().EndPosition : null;

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		SourceWeaponSetup.ConfigureAmmo( this );
		return base.OnAdding( inventory );
	}

	protected override void OnStart()
	{
		base.OnStart();
		_ = SourceWeaponSetup.ConfigureWeapon( this );
	}

	public override bool CanPrimaryAttack() => !_releasePending && !_redrawPending && !LiveRocket.IsValid() && base.CanPrimaryAttack();
	// CWeaponRPG reloads only once its missile is gone (NotifyRocketDied).
	public override bool CanReload() => !_releasePending && !_redrawPending && !LiveRocket.IsValid() && base.CanReload();
	public override bool CanSecondaryAttack() => Secondary is SecondaryFires.Zoom or SecondaryFires.Guide && base.CanSecondaryAttack();

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( 0.3f );
		if ( Secondary == SecondaryFires.Guide ) ToggleGuiding();
		else _zoomed = !_zoomed;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void ToggleGuiding()
	{
		Guiding = !Guiding;
		GuidingSound( Guiding );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void GuidingSound( bool on )
	{
		if ( SourceSounds.WeaponSound( this, on ? "special1" : "special2" ) is { } sound ) GameObject.PlaySound( sound );
	}

	/// <summary>Host only: CWeaponRPG::NotifyRocketDied reloads the launcher.</summary>
	internal void NotifyRocketDied()
	{
		LiveRocket = null;
		if ( !IsValid ) return;
		if ( IsHeld && AutoReload && CanReload() ) Reload();
	}

	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		base.ModifyCamera( camera, ref view );
		if ( _zoomed ) view.FieldOfView = SourceZoom.FieldOfView;
	}

	protected override void OnReloadStarted()
	{
		base.OnReloadStarted();
		if ( WeaponModel is not SourceViewModel ) SourceSounds.PlayReloadSound( this, GameObject );
	}

	public override void DrawCrosshair( Painter painter, Vector2 center ) => SourceCrosshair.Draw( painter, center, CanPrimaryAttack(), SpreadBloom );

	protected override void OnHolstered()
	{
		_zoomed = false;
		_releasePending = false;
		_releaseAttacker = null;
		_redrawPending = false;
		base.OnHolstered();
	}

	protected override void OnDisabled()
	{
		_laser?.Dispose();
		_laser = null;
		base.OnDisabled();
	}

	public override void PrimaryAttack()
	{
		if ( _releasePending || _redrawPending ) return;
		if ( ReleaseDelay > 0 )
		{
			_releasePending = true;
			_releaseAttacker = Attacker;
			_releaseAt = ReleaseDelay;
			_redrawAt = AttackDuration;
			SetNextFire( Math.Max( PrimaryDelay, AttackDuration + RedrawDuration ) );
			ShootEffects();
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
		if ( !IsLauncher || Application.IsDedicatedServer ) return;
		_laser ??= new Hl2RpgLaser();
		_laser.Update( this, LaserOn );
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
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
		if ( _redrawPending && _redrawAt <= 0 )
		{
			_redrawPending = false;
			if ( IsHeld && AutoReload && CanReload() ) Reload();
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Launch( Vector3 eye, Vector3 forward, GameObject attacker )
	{
		var damage = GetComponentInParent<Sandbox.Npcs.BaseNpc>().IsValid() ? NpcDamage ?? ProjectileDamage : ProjectileDamage;
		var inherited = Owner?.Velocity ?? Vector3.Zero;
		var (origin, velocity) = Kind switch
		{
			SourceProjectile.Kinds.Grenade => (ThrowOrigin( eye, forward, 8 ), Hl2Grenade.ThrowVelocity( forward, inherited )),
			SourceProjectile.Kinds.Bugbait => (ThrowOrigin( eye, forward, 12 ), Hl2Bugbait.ThrowVelocity( forward, inherited )),
			SourceProjectile.Kinds.HeGrenade or SourceProjectile.Kinds.Flashbang or SourceProjectile.Kinds.SmokeGrenade => (ThrowOrigin( eye, forward, 0 ), CssRules.ThrowVelocity( forward, inherited )),
			SourceProjectile.Kinds.Rocket => (eye, Hl2Rocket.LaunchVelocity( forward )),
			_ => (eye, forward * Hl2CrossbowBolt.AirVelocity)
		};
		var projectile = SourceProjectile.Launch( this, ResolveLaunchAttacker( attacker ), Kind, origin, velocity, damage, ExplosionRadius, ProjectileModel );
		if ( IsLauncher ) LiveRocket = projectile.GameObject;
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
