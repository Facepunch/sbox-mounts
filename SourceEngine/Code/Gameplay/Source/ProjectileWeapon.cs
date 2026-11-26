using System;

namespace Source1;

/// <summary>Shared physical projectile weapon; the catalog supplies ballistics and mounted art.</summary>
[Title( "Source Projectile Weapon" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceProjectileWeapon", "SourceProjectileWeapon" )]
public sealed partial class ProjectileWeapon : Weapon
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
	internal WeaponClips ThrowClips { get; set; }
	enum ThrowStyle { Overhand, Lob, Roll }
	ThrowStyle _throwStyle;
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

	// MAX_TRACE_LENGTH is the diagonal of the Source world coordinate cube.
	const float MaxTraceLength = 56755.84f;

	bool IsLauncher => Kind == Projectile.Kinds.Rocket;
	bool LaserOn => IsLauncher && Guiding && Active && !IsReloading;

	/// <summary>Where the laser dot sits; the live rocket steers toward it.</summary>
	public Vector3? TraceLaserTarget() => TraceLaser()?.EndPosition;

	SceneTraceResult? TraceLaser() => LaserOn ? Scene.Trace.Ray( AimRay, MaxTraceLength ).IgnoreGameObjectHierarchy( AimIgnoreRoot )
		.WithoutTags( "projectile", "trigger", "source1_window" ).Run() : null;

	internal Vector3? MissileTarget( Vector3 missile )
	{
		if ( TraceLaser() is not { } trace || missile.DistanceSquared( trace.EndPosition ) >= MaxTraceLength * MaxTraceLength ) return null;
		var target = trace.Hit ? trace.EndPosition - trace.Normal * 10 : trace.EndPosition;
		if ( trace.GameObject.IsValid() && trace.GameObject.GetComponentInParent<IDamageable>() is not null ) return target;

		var start = Npcs.CapturePoint( AimIgnoreRoot );
		var beam = target - start;
		var offset = missile - start;
		if ( offset.LengthSquared >= beam.LengthSquared && missile.DistanceSquared( target ) > 512 * 512 ) return target;

		// Aim 256 units along the beam from the missile's projection onto its line.
		var direction = beam.Normal;
		return start + direction * (offset.Dot( direction ) + 256);
	}

	protected override void OnStart()
	{
		base.OnStart();
		_ = WeaponSetup.ConfigureWeapon( this );
	}

	public override bool CanPrimaryAttack() => (!_primed || HasLowThrow) && !_releasePending && !_redrawPending && !LiveRocket.IsValid() && base.CanPrimaryAttack();
	// CWeaponRPG reloads only once its missile is gone (NotifyRocketDied).
	public override bool CanReload() => !_primed && !_releasePending && !_redrawPending && !LiveRocket.IsValid() && base.CanReload();
	internal override bool ShouldPlayIdle => Kind != Projectile.Kinds.Bolt || !AttackOrReloadHeld;

	bool _reloadAfterShot;
	protected override bool WantsReload()
	{
		if ( AutoReloadDelay <= 0 ) return base.WantsReload();
		if ( Input.Down( "reload" ) && NextPrimaryFire <= 0 ) return true;
		return AutoReload && Clip1 == 0 && TimeSinceShoot >= AutoReloadDelay && Ammo1 > 0
			&& (_reloadAfterShot || !Input.Down( "attack2" ));
	}

	bool HasLowThrow => Kind == Projectile.Kinds.Grenade && ThrowClips?.LowWindup is not null
		&& ThrowClips.Lob.Clip is not null && ThrowClips.Roll.Clip is not null;

	public override bool CanSecondaryAttack()
	{
		if ( Secondary == SecondaryFires.Zoom ) return true;
		if ( !base.CanSecondaryAttack() ) return false;
		if ( HasLowThrow ) return !_releasePending && !_redrawPending && HasPrimaryAmmo();
		return Secondary == SecondaryFires.Guide || (Kind == Projectile.Kinds.Bugbait && !_primed && !_redrawPending);
	}

	public override void SecondaryAttack()
	{
		if ( HasLowThrow )
		{
			PrimeThrow( true );
			return;
		}
		if ( Kind == Projectile.Kinds.Bugbait )
		{
			if ( _releasePending )
			{
				// The squeeze replaces the throw sequence before its projectile event.
				_releasePending = false;
				_releaseAttacker = null;
				SetNextPrimaryFire( _redrawAt );
			}
			SqueezeEffects();
			return;
		}
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

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void SqueezeEffects()
	{
		if ( Networking.IsHost && !Hl2.BugbaitSensor.Activate( Scene, WorldPosition, Attacker, squeezed: true ) )
			Hl2.AntlionMaker.OrderSquads( Scene, Attacker, null );
		(WeaponModel as ViewModel)?.OnSqueeze();
		if ( Sounds.Sound( "Weapon_Bugbait.Splat", MountContent.OfWeapon( this ) ) is { } sound )
			GameObject.PlaySound( sound );
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
		_reloadAfterShot = false;
		// The clipless RPG plays a weapon animation on impact without a player reload gesture.
		if ( IsLauncher && !UsesPrimaryClip ) WeaponModel?.OnReloadStart();
		else base.OnReloadStarted();
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
		if ( _primed && HasLowThrow ) SetNextFire( 0 );
		_primed = false;
		_releasePending = false;
		_releaseAttacker = null;
		_redrawPending = false;
		base.OnHolstered();
		if ( Networking.IsHost && IsSpentFrag ) _ = RetireFrag( Inventory );
	}

	bool IsSpentFrag => Kind == Projectile.Kinds.Grenade && HasOwner && Inventory.IsValid() && !HasPrimaryAmmo();

	async Task RetireFrag( BaseInventoryComponent inventory )
	{
		await GameTask.DelaySeconds( 0.1f );
		if ( !this.IsValid() || !inventory.IsValid() || Inventory != inventory ) return;
		if ( Active ) inventory.Remove( this );
		else GameObject.Destroy();
	}

	protected override void OnDisabled()
	{
		StopBaitTrail();
		ReleaseZoom();
		_laser?.Dispose();
		_laser = null;
		base.OnDisabled();
	}

	Hl2.SporeCloud _baitTrail;

	void StopBaitTrail()
	{
		if ( _baitTrail.IsValid() ) _baitTrail.StopEmitting();
		_baitTrail = null;
	}

	public override void PrimaryAttack()
	{
		if ( (_primed && (!HasLowThrow || _throwStyle == ThrowStyle.Overhand)) || _releasePending || _redrawPending ) return;
		if ( ThrowClips is not null )
		{
			PrimeThrow( false );
			return;
		}
		if ( UsesAmmo && HasOwner && !TakePrimaryAmmo( 1 ) )
		{
			DryFire();
			return;
		}
		TimeSinceShoot = 0;
		_reloadAfterShot = AutoReloadDelay > 0;
		SetNextFire( PrimaryDelay );
		ShootEffects();
		KickView();
		Launch( AimRay.Position, AimRay.Forward, Attacker );
	}

	void PrimeThrow( bool low )
	{
		if ( Kind == Projectile.Kinds.Grenade && BlockSubmergedShot( blockBothTriggers: low, playEmptySound: !low ) ) return;

		// Each frag trigger locks itself until release. The other can still change the pullback.
		if ( HasLowThrow )
		{
			if ( low ) SetNextSecondaryFire( float.MaxValue );
			else SetNextPrimaryFire( float.MaxValue );
		}
		_throwStyle = low ? ThrowStyle.Lob : ThrowStyle.Overhand;
		_releaseAttacker = Attacker;
		if ( Kind == Projectile.Kinds.Grenade ) ThrowEffects( low ? ThrowClips.LowWindup : ThrowClips.ThrowWindup );
		else ShootEffects();
		if ( (low ? ThrowClips.LowWindupDuration : ThrowClips.WindupDuration) is { } windup )
		{
			_primed = true;
			_releaseAt = windup;
		}
		else BeginThrow();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Kind == Projectile.Kinds.Bugbait && !Application.IsDedicatedServer )
		{
			if ( WasDropped && !IsHeld )
				_baitTrail ??= Hl2.SporeCloud.Create( WorldPosition, Vector3.Up, MountContent.OfWeapon( this ), GameObject );
			else if ( _baitTrail.IsValid() )
				StopBaitTrail();
		}
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

	protected override void OnControl()
	{
		base.OnControl();
		if ( _primed && _releaseAt <= 0 && !Input.Down( _throwStyle == ThrowStyle.Overhand ? "attack1" : "attack2" ) ) BeginThrow();
		ReleaseIfReady();
	}

	protected override void Think()
	{
		base.Think();
		// Frag checks secondary first and can start the high pullback on the same tick.
		if ( HasLowThrow && WantsSecondaryAttack() ) FireSecondary();
		if ( IsLauncher && _redrawPending && _redrawAt <= 0 && !AttackOrReloadHeld ) FinishRedraw();
	}

	void BeginThrow()
	{
		if ( _throwStyle == ThrowStyle.Lob && Owner?.DuckButton is { Length: > 0 } duck && Input.Down( duck ) )
			_throwStyle = ThrowStyle.Roll;
		var release = _throwStyle switch
		{
			ThrowStyle.Roll => ThrowClips.Roll,
			ThrowStyle.Lob => ThrowClips.Lob,
			_ => ThrowClips.PrimaryThrow
		};
		if ( _primed ) ThrowEffects( release.Clip, released: true );
		_primed = false;
		_releasePending = true;
		_releaseAt = release.Delay;
		_redrawAt = _throwStyle == ThrowStyle.Overhand ? release.Duration : release.Delay + Hl2.Grenade.RethrowDelay;
		var delay = Math.Max( PrimaryDelay, (float)_redrawAt + RedrawDuration );
		if ( Kind == Projectile.Kinds.Bugbait ) SetNextPrimaryFire( delay );
		else SetNextFire( delay );
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void ThrowEffects( string clip, bool released = false )
	{
		(WeaponModel as ViewModel)?.OnThrow( clip );
		if ( released && Kind == Projectile.Kinds.Grenade ) PostWeaponEvent( WeaponEvents.Attack );
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( _dropVelocity.HasValue ) MoveDroppedBait();
		if ( Networking.IsHost && IsLauncher && Active && Secondary != SecondaryFires.Guide )
			SetGuiding( !HasOwner || (!IsDeploying && (HasPrimaryAmmo() || LiveRocket.IsValid())) );
		if ( _primed && !HasOwner && _releaseAt <= 0 ) BeginThrow();
		ReleaseIfReady();
		if ( _redrawPending && _redrawAt <= 0 && (!IsLauncher || !HasOwner || IsProxy) ) FinishRedraw();
	}

	void ReleaseIfReady()
	{
		if ( !_releasePending || _releaseAt > 0 ) return;
		var attacker = _releaseAttacker;
		_releasePending = false;
		_releaseAttacker = null;
		// A primed frag still leaves the hand if its reserve was drained during pullback.
		if ( UsesAmmo && IsHeld && !TakePrimaryAmmo( 1 ) && Kind != Projectile.Kinds.Grenade ) return;
		TimeSinceShoot = 0;
		KickView();
		Launch( AimRay.Position, AimRay.Forward, attacker, _throwStyle );
		_redrawPending = true;
	}

	void FinishRedraw()
	{
		_redrawPending = false;
		if ( IsLauncher && !UsesPrimaryClip ) OnReloadFinished();
		else if ( IsHeld && AutoReload && CanReload() ) Reload();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Launch( Vector3 eye, Vector3 forward, GameObject attacker, ThrowStyle style = ThrowStyle.Overhand )
	{
		var damage = GetComponentInParent<Sandbox.Npcs.BaseNpc>().IsValid() ? NpcDamage ?? ProjectileDamage : ProjectileDamage;
		var inherited = Owner?.Velocity ?? Vector3.Zero;
		var (origin, velocity) = Kind switch
		{
			Projectile.Kinds.Grenade when style == ThrowStyle.Roll && Owner.IsValid() => RollLaunch( forward, inherited ),
			Projectile.Kinds.Grenade when style == ThrowStyle.Lob => (ThrowOrigin( eye, forward, 8, -8 ), inherited + forward * 350 + Vector3.Up * 50),
			Projectile.Kinds.Grenade => (ThrowOrigin( eye, forward, 8 ), Hl2.Grenade.ThrowVelocity( forward, inherited )),
			Projectile.Kinds.Bugbait => (eye + forward * 18 + Rotation.LookAt( forward ).Right * 12, Hl2.Bugbait.ThrowVelocity( forward, inherited )),
			Projectile.Kinds.HeGrenade or Projectile.Kinds.Flashbang or Projectile.Kinds.SmokeGrenade => (ThrowOrigin( eye, forward, 0 ), Css.Rules.ThrowVelocity( forward, inherited )),
			Projectile.Kinds.Rocket => (eye + Rotation.LookAt( forward ) * new Vector3( 12, -6, -3 ), Hl2.Rocket.LaunchVelocity( forward )),
			_ => (eye, Hl2.CrossbowBolt.LaunchVelocity( Scene, eye, forward ))
		};
		var radius = IsLauncher ? damage * 2 : ExplosionRadius;
		var projectile = Projectile.Launch( this, ResolveLaunchAttacker( attacker ), Kind, origin, velocity, damage, radius, ProjectileModel );
		if ( projectile is Hl2.Grenade grenade && style != ThrowStyle.Overhand )
		{
			var roll = style == ThrowStyle.Roll;
			grenade.WorldRotation = roll ? Rotation.From( 0, Rotation.LookAt( forward ).Yaw(), -90 ) : Rotation.Identity;
			grenade.GetComponent<Rigidbody>().AngularVelocity = grenade.WorldRotation * (roll ? new Vector3( 0, 0, 720 )
				: new Vector3( 200, Game.Random.Int( -600, 600 ), 0 )) * (MathF.PI / 180);
			ThrowSound( roll ? "special1" : "double_shot" );
		}
		if ( projectile is Hl2.Rocket rocket )
		{
			rocket.ClearLaunch = HasClearLaunch( eye, forward );
			LiveRocket = rocket.GameObject;
		}
		else if ( projectile is Hl2.Bugbait bait )
			bait.ClearLaunch = HasClearLaunch( eye, forward );

		if ( IsSpentFrag )
		{
			Inventory.SwitchToBest();
			if ( Active ) _ = RetireFrag( Inventory );
		}
	}

	bool HasClearLaunch( Vector3 eye, Vector3 forward ) => !Scene.Trace.Ray( eye, eye + forward * 128 )
		.IgnoreGameObjectHierarchy( AimIgnoreRoot ).WithoutTags( "projectile", "trigger" ).UseHitboxes().Run().Hit;

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ThrowSound( string role )
	{
		if ( Sounds.WeaponSound( this, role ) is { } sound ) GameObject.PlaySound( sound );
	}

	(Vector3 Origin, Vector3 Velocity) RollLaunch( Vector3 forward, Vector3 inherited )
	{
		var feet = Owner.WorldPosition + Vector3.Up * 4;
		var facing = forward.WithZ( 0 ).Normal;
		var floor = Scene.Trace.Ray( feet, feet + Vector3.Down * 16 ).IgnoreGameObjectHierarchy( AimIgnoreRoot ).WithoutTags( "trigger" ).Run();
		if ( floor.Hit ) facing -= floor.Normal * facing.Dot( floor.Normal );
		var center = Owner.WorldPosition + Vector3.Up * (Owner.CurrentHeight * 0.5f);
		return (CheckThrowPosition( center, feet + facing * 18 ), inherited + facing * 700);
	}

	Vector3 ThrowOrigin( Vector3 eye, Vector3 forward, float right, float up = 0 )
	{
		return CheckThrowPosition( eye, eye + forward * 18 + Rotation.LookAt( forward ).Right * right + Vector3.Up * up );
	}

	Vector3 CheckThrowPosition( Vector3 eye, Vector3 target )
	{
		var tr = Scene.Trace.Box( BBox.FromPositionAndSize( Vector3.Zero, 12 ), eye, target )
			.IgnoreGameObjectHierarchy( AimIgnoreRoot ).WithoutTags( "trigger", "ragdoll", "projectile" ).Run();
		return tr.Hit ? tr.EndPosition : target;
	}
}
