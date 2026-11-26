using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>The sandbox rifles' four arms, opening with spread bloom; Source weapons draw no crosshair of their own.</summary>
static class Crosshair
{
	public static void Draw( Painter painter, Vector2 center, bool ready, float bloom )
	{
		using var scope = painter.Scope();
		var gap = 16 + bloom * 32;
		var length = 12;
		painter.BlendMode = BlendMode.Lighten;
		painter.Stroke = Stroke.Solid( ready ? Color.White : Color.Red, 2 );
		painter.Line( center + Vector2.Left * (length + gap), center + Vector2.Left * gap );
		painter.Line( center - Vector2.Left * (length + gap), center - Vector2.Left * gap );
		painter.Line( center + Vector2.Up * (length + gap), center + Vector2.Up * gap );
		painter.Line( center - Vector2.Up * (length + gap), center - Vector2.Up * gap );
	}
}

/// <summary>Counter-Strike's sniper scope: black outside the lens, with thin cross lines through it.</summary>
static class Scope
{
	public static void Draw( Painter painter, Vector2 size )
	{
		using var scope = painter.Scope();
		var center = size * 0.5f;
		var radius = MathF.Min( size.x, size.y ) * 0.5f;
		painter.BlendMode = BlendMode.Normal;
		painter.Stroke = Stroke.None;
		painter.Fill = Color.Black;
		painter.Ring( center, radius, size.Length );
		painter.Fill = Color.Transparent;
		painter.Stroke = Stroke.Solid( Color.Black, 1 );
		painter.Line( center - new Vector2( radius, 0 ), center + new Vector2( radius, 0 ) );
		painter.Line( center - new Vector2( 0, radius ), center + new Vector2( 0, radius ) );
	}
}

/// <summary>Shared mounted hitscan weapon; firing/reloading rules and presentation are prefab data.</summary>
[Title( "Source Bullet Weapon" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceBulletWeapon", "SourceBulletWeapon" )]
public sealed class BulletWeapon : Weapon
{
	/// <summary>Physical kick applied to a standalone (unheld) gun when it fires.</summary>
	[Property, Group( "Recoil" )] public float ShootForce { get; set; } = 100000f;

	/// <summary>Draw a tracer every N eligible bullets. Zero disables tracers.</summary>
	[Property, Feature( "Shooting" )] public int TracerFrequency { get; set; } = 2;
	static uint _tracerShots;
	bool _remoteVolleyTracer;

	/// <summary>
	/// Scale of the bullet impact effect on a client-side ragdoll.
	/// </summary>
	[Property, Group( "Ragdoll Impact" ), Range( 0f, 10f )] public float RagdollImpactScale { get; set; } = 1f;

	/// <summary>Optional fraction of the impact impulse applied to each remaining ragdoll body.</summary>
	[Property, Group( "Ragdoll Impact" ), Range( 0f, 1f )] public float RagdollBodyShare { get; set; }

	// weapon_ar2 and weapon_smg1 launch their alt fire at 1000 units/s; the damage and radius
	// fall back to the SDK's sk_plr_dmg_smg1_grenade and sk_smg1_grenade_radius.
	const float AlternateSpeed = 1000;
	const float DefaultAlternateDamage = 100;
	const float DefaultAlternateRadius = 250;
	// weapon_ar2 DelayedAttack fires the ball half a second after the charge starts.
	const float EnergyBallWindup = 0.5f;
	// weapon_shotgun SecondaryAttack fires a literal 12 pellets.
	const int DoubleShotPellets = 12;

	[Property, Feature( "Shooting" )] public bool FiresUnderwater { get; set; } = true;
	/// <summary>Minimum interval between separate trigger pulls for weapons that fire more slowly when held.</summary>
	[Property, Feature( "Shooting" )] public float TapRefireDelay { get; set; }
	[Property, Feature( "Shooting" )] public float DryTapRefireDelay { get; set; }
	internal float DryFireDuration { get; set; }
	internal (float Delay, string Mount)? CylinderEjection { get; set; }
	TimeUntil? _ejectCylinderAt;
	bool _holdingEmptyTrigger;
	/// <summary>Seconds of spread recovery added by each shot. The HL2 pistol recovers while the trigger is released.</summary>
	[Property, Feature( "Shooting" )] public float AccuracyPenaltyPerShot { get; set; }
	float _accuracyPenalty;
	public override Vector2 CurrentSpread => AccuracyPenaltyPerShot > 0 && HasOwner && Ballistics.SpreadRecovery > 0
		? (Ballistics.SpreadBase + Ballistics.SpreadGrowth * Math.Min( _accuracyPenalty / Ballistics.SpreadRecovery, 1f )) * SpreadScale
		: base.CurrentSpread;
	/// <summary>Keep automatic rounds on their scheduled interval when a frame crosses a shot deadline.</summary>
	[Property, Feature( "Shooting" )] public bool ContinuousFireRate { get; set; }
	/// <summary>Maximum primary-fire kick in degrees and seconds of held fire to reach it.</summary>
	[Property, Group( "Recoil" )] public Vector2 MachineGunKick { get; set; }
	float _fireDuration;
	double _lastMachineGunKickTime = double.NaN;
	TimeUntil _reloadInputDelay;
	TimeUntil? _tapReady;

	[Property] public SecondaryFires Secondary { get; set; }
	/// <summary>Field of view of each <see cref="SecondaryFires.Zoom"/> step; empty zooms once to <see cref="Zoom.FieldOfView"/>.</summary>
	[Property] public List<float> ZoomLevels { get; set; }
	[Sync] public bool BurstMode { get; set; }
	[Sync, Change( nameof( OnSilencedChanged ) )] public bool Silenced { get; set; }
	internal float SilencerDuration { get; set; }
	/// <summary>True while firing the second and later rounds of a burst.</summary>
	internal bool BurstFollowUp { get; private set; }
	internal float? AlternateDamage { get; set; }
	internal float? AlternateRadius { get; set; }
	[Property, Feature( "Shooting" )] public bool CanLaunchDuringReload { get; set; }
	[Property, Feature( "Shooting" )] public float PrimaryDelayAfterLaunch { get; set; } = 1;
	internal Projectile.Kinds? AlternateProjectile { get; set; }
	internal float ReloadFinishDuration { get; set; }
	internal float AlternatePumpDelay { get; set; }
	internal float PumpDuration { get; set; } = 1;
	internal bool NpcControlled => Holder is BaseNpc;
	// The host picks the NPC's reload clip and shares its duration for animation on other peers.
	[Sync( SyncFlags.FromHost )] internal float NpcReloadAnimationTime { get; private set; }
	(float Duration, bool Incremental)? _playerReloadSettings;
	bool _weaponConfigured;
	bool? _tunedForNpc;
	bool _alternatePending;
	bool _queuedPrimary, _queuedSecondary;
	BaseInventoryComponent _queuedShotInventory;
	internal bool ShotQueued => _queuedPrimary || _queuedSecondary;
	TimeUntil? _pumpStarts;
	bool _reloadOnPump;
	bool _lastShellInserting;
	internal bool ReloadingDuringPump { get; private set; }
	bool _pumpOnDeploy;
	TimeUntil _alternateAt;
	GameObject _alternateAttacker;
	int _zoomLevel;
	int _burstRemaining;
	TimeUntil _burstAt;
	GameObject _burstAttacker;
	bool _scoped;

	bool Launches => Secondary == SecondaryFires.Default && AlternateProjectile is not null;
	bool BlocksUnusedSecondaryUnderwater => !FiresUnderwater && Secondary == SecondaryFires.Default && AlternateProjectile is null;

	protected override GameObject Attacker => BurstFollowUp && _burstAttacker.IsValid() ? _burstAttacker : base.Attacker;

	public override bool CanPrimaryAttack() => !_alternatePending && _burstRemaining <= 0
		&& (!NpcControlled || _weaponConfigured) && base.CanPrimaryAttack();

	public override bool CanReload() => !_alternatePending && (!NpcControlled || _weaponConfigured) && base.CanReload();

	protected override bool WantsPrimaryAttack()
	{
		if ( Secondary == SecondaryFires.DoubleShot && WantsSecondaryAttack() && (!IsReloading || HasSecondaryAmmo()) ) return false;
		return _queuedPrimary || base.WantsPrimaryAttack();
	}
	protected override bool WantsReload()
	{
		if ( !UsesPrimaryClip ) return base.WantsReload();
		if ( NextPrimaryFire > 0 ) return false;
		if ( Input.Down( "reload" ) )
		{
			if ( IncrementalReloading && WantsSecondaryAttack() && CanSecondaryAttack() ) return false;
			return !(WantsPrimaryAttack() && CanPrimaryAttack());
		}
		return !IncrementalReloading && AutoReload && Clip1 == 0 && NextSecondaryFire <= 0 && !Input.Down( "attack1" ) && !Input.Down( "attack2" );
	}
	protected override bool WantsSecondaryAttack() => _queuedSecondary || (!Launches && base.WantsSecondaryAttack());

	protected override void OnControl()
	{
		// Source checks water before dispatching even an unimplemented secondary attack.
		if ( BlocksUnusedSecondaryUnderwater && !IsDeploying && !IsReloading && NextSecondaryFire <= 0 && Input.Down( "attack2" )
			&& BlockSubmergedShot( blockBothTriggers: true, playEmptySound: false ) ) return;
		base.OnControl();
		if ( Secondary != SecondaryFires.DelayPrimary || IsReloading || !Input.Down( "attack2" ) ) return;

		_tapReady = (TimeUntil)TapRefireDelay;
		SetNextPrimaryFire( TapRefireDelay );
		if ( WeaponModel is ViewModel model ) model.DelayRecoilReset( TapRefireDelay );
	}

	void UpdateFireDuration()
	{
		// A holstered or drawing weapon receives no attack commands, so its previous hold is preserved.
		if ( IsDeploying || (IsReloading && (!CanLaunchDuringReload || _reloadInputDelay > 0)) ) return;
		if ( !Input.Down( "attack1" ) )
			_fireDuration = 0;
		else
			// The first attack command contributes one movement tick, regardless of the render frame before it.
			_fireDuration = _fireDuration == 0 ? ViewPunch.Tick : _fireDuration + Time.Delta;
	}

	protected override void Think()
	{
		base.Think();
		UpdateFireDuration();
		// A ready launcher takes priority over bullets and reload on the same frame.
		if ( Launches && base.WantsSecondaryAttack() && NextSecondaryFire <= 0 && (!IsReloading || CanLaunchDuringReload) )
		{
			if ( HasSecondaryAmmo() ) FireSecondary();
			else if ( TryPlayEmptyClick( 0.5f ) ) SetNextSecondaryFire( 0.5f );
		}
		if ( _holdingEmptyTrigger && !Input.Down( "attack1" ) ) _holdingEmptyTrigger = false;
		if ( _pumpOnDeploy && !IsDeploying )
		{
			_pumpOnDeploy = false;
			_reloadOnPump |= Input.Down( "reload" );
			SetNextFire( PumpDuration );
			PumpEffects();
		}
		if ( _pumpStarts is { } pumpStarts )
		{
			_reloadOnPump |= Input.Down( "reload" );
			if ( pumpStarts <= 0 ) _pumpStarts = null;
		}
		if ( _reloadOnPump && !_pumpOnDeploy && !_pumpStarts.HasValue && NextPrimaryFire <= ReloadStartTime )
		{
			_reloadOnPump = false;
			if ( CanReload() )
			{
				ReloadingDuringPump = true;
				Reload();
			}
		}
		if ( Secondary == SecondaryFires.DoubleShot && (IsReloading || _lastShellInserting) && !ShotQueued )
		{
			// Finish the current shell insert before firing the trigger that interrupted it.
			_queuedPrimary = Clip1 >= 1 && base.WantsPrimaryAttack();
			_queuedSecondary = !_queuedPrimary && Clip1 >= 2 && base.WantsSecondaryAttack();
		}
		if ( _lastShellInserting && NextPrimaryFire <= 0 )
		{
			_lastShellInserting = false;
			if ( !ShotQueued ) SetNextFire( ReloadFinishDuration );
		}
		if ( Secondary == SecondaryFires.DoubleShot && Clip1 == 0 && NextPrimaryFire <= 0 && AutoReload && CanReload() )
		{
			ConsumeEmptyShot();
			Reload();
		}
		if ( ContinuousFireRate && PrimaryDelay > 0 && Input.Down( "attack1" ) && !Input.Pressed( "attack1" ) && !WantsReload() )
		{
			// Only catch up time spent holding the trigger during this frame.
			var remaining = Math.Max( (float)NextPrimaryFire, -Time.Delta );
			while ( remaining <= 0 )
			{
				var rounds = Clip1;
				if ( !FirePrimary() || (UsesPrimaryClip && Clip1 == rounds) ) break;
				remaining += PrimaryDelay;
				SetNextPrimaryFire( remaining );
			}
		}
		if ( _accuracyPenalty <= 0 && !_tapReady.HasValue ) return;
		if ( Input.Down( "attack1" ) || _tapReady is { } pending && pending > 0 ) return;
		if ( _accuracyPenalty > 0 )
			_accuracyPenalty = Math.Max( 0f, Math.Min( _accuracyPenalty - Time.Delta, Ballistics.SpreadRecovery ) );
		if ( !_tapReady.HasValue ) return;
		_tapReady = null;
		SetNextPrimaryFire( 0 );
	}

	protected override bool OnHolstering( BaseInventoryItem next ) => !_alternatePending && base.OnHolstering( next );

	protected override void OnHolstered()
	{
		if ( (IsReloading || _lastShellInserting) && !ShotQueued && MountContent.OfWeapon( this ) == "hl2" ) SetNextFire( 0 );
		_holdingEmptyTrigger = false;
		_lastShellInserting = false;
		if ( ShotQueued && Inventory is { } inventory && inventory.ActiveItem is Weapon && Slot >= 0 )
		{
			_queuedShotInventory = inventory;
			inventory.ActiveItemChanged += OnQueuedShotSwitch;
		}
		else _queuedPrimary = _queuedSecondary = false;
		_tapReady = null;
		_pumpOnDeploy |= _pumpStarts.HasValue;
		_pumpStarts = null;
		if ( !_pumpOnDeploy ) _reloadOnPump = false;
		ReloadingDuringPump = false;
		_alternatePending = false;
		_alternateAttacker = null;
		CancelBurst();
		SetZoom( 0 );
		base.OnHolstered();
	}

	// The holstered shotgun clears its delayed shot once the active weapon finishes drawing.
	void OnQueuedShotSwitch( BaseInventoryItem previous, BaseInventoryItem next )
	{
		if ( next is not Weapon || (previous != this && previous is not Weapon { IsDeploying: true }) )
			_queuedPrimary = _queuedSecondary = false;

		if ( !ShotQueued || next == this ) StopWatchingSwitches();
	}

	void StopWatchingSwitches()
	{
		if ( _queuedShotInventory is null ) return;
		_queuedShotInventory.ActiveItemChanged -= OnQueuedShotSwitch;
		_queuedShotInventory = null;
	}

	protected override void OnParentChanged( GameObject oldParent, GameObject newParent )
	{
		base.OnParentChanged( oldParent, newParent );
		_queuedPrimary = _queuedSecondary = false;
		StopWatchingSwitches();
	}

	protected override void OnDestroy()
	{
		StopWatchingSwitches();
		base.OnDestroy();
	}

	protected override void OnDisabled()
	{
		CancelBurst();
		SetZoom( 0 );
		base.OnDisabled();
	}

	protected override void OnReloadStarted()
	{
		// An interruptible reload preserves the launcher's existing attack deadline.
		_reloadInputDelay = NextSecondaryFire;
		_fireDuration = 0;
		_holdingEmptyTrigger = false;
		_accuracyPenalty = 0;
		_tapReady = null;
		SetZoom( 0 );
		CancelBurst();
		var animator = GetComponentInParent<NpcAnimator>();
		if ( !Application.IsDedicatedServer && HasOwner && CylinderEjection is { } cylinder ) _ejectCylinderAt = (TimeUntil)cylinder.Delay;
		if ( Networking.IsHost && NpcControlled )
		{
			// The engine reads these settings after this callback. NPCs fill the magazine
			// when their animation ends, including when the animation loops for each shell.
			_playerReloadSettings = (ReloadTime, IncrementalReloading);
			var duration = animator.IsValid() ? animator.ReloadDuration( ClipMaxSize - Clip1 ) : null;
			NpcReloadAnimationTime = ReloadTime = Math.Max( duration ?? ReloadTime, 0.1f );
			IncrementalReloading = false;
		}
		if ( Secondary == SecondaryFires.DoubleShot && !NpcControlled ) SetNextFire( ReloadStartTime );
		base.OnReloadStarted();
		if ( !IncrementalReloading && !animator.IsValid() && WeaponModel is not Source1.ViewModel ) Sounds.PlayWeaponSound( this, "reload" );
	}

	protected override void OnReloadFinished()
	{
		ReloadingDuringPump = false;
		_ejectCylinderAt = null;
		if ( _playerReloadSettings is { } settings )
		{
			(ReloadTime, IncrementalReloading) = settings;
			_playerReloadSettings = null;
		}
		base.OnReloadFinished();
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( _ejectCylinderAt is { } ejectAt && ejectAt <= 0 )
		{
			_ejectCylinderAt = null;
			if ( HasOwner && IsReloading && CylinderEjection is { } cylinder )
				WeaponEffects.EjectCylinder( Owner.WorldPosition + Vector3.Up * (Owner.CurrentHeight * 0.5f), WorldRotation, cylinder.Mount );
		}
		var npc = NpcControlled;
		if ( npc != _tunedForNpc && WeaponSetup.ApplyHolder( this, npc ) ) _tunedForNpc = npc;
		if ( npc && Networking.IsHost && !IsReloading && !HasPrimaryAmmo() ) Reload();
		if ( IsProxy ) return;

		UpdateBurst();
		if ( !_alternatePending || _alternateAt > 0 ) return;
		var attacker = _alternateAttacker;
		_alternatePending = false;
		_alternateAttacker = null;
		ReleaseAlternate( attacker );
	}

	public override void PrimaryAttack()
	{
		// The engine attack spends the round for an NPC holder too.
		if ( NpcControlled && !Networking.IsHost ) return;
		_queuedPrimary = false;
		if ( !FiresUnderwater && BlockSubmergedShot( Secondary == SecondaryFires.DoubleShot ) ) return;
		FirePrimaryRound();
	}

	void ConsumeEmptyShot()
	{
		if ( !ShotQueued ) return;
		if ( WantsSecondaryAttack() ) _queuedSecondary = false;
		else _queuedPrimary = false;
	}

	public override void DryFire()
	{
		ConsumeEmptyShot();
		var clickOnly = WantsEmptyClick;
		if ( !HasOwner || (!clickOnly && DryTapRefireDelay <= 0) )
		{
			base.DryFire();
			if ( HasOwner && Secondary == SecondaryFires.DoubleShot && !IsReloading && DryFireDuration > 0 )
				SetNextFire( DryFireDuration );
			return;
		}
		if ( _holdingEmptyTrigger && AutoReload && CanReload() )
		{
			Reload();
			return;
		}
		if ( _holdingEmptyTrigger && clickOnly )
		{
			_holdingEmptyTrigger = false;
			_fireDuration = 0;
			return;
		}
		_holdingEmptyTrigger = true;
		if ( clickOnly )
		{
			TryPlayEmptyClick( EmptyClickInterval );
			return;
		}
		PlayEmptySound();
		WeaponModel?.OnDryFire();
		SetNextPrimaryFire( Math.Max( DryTapRefireDelay, DryFireDuration ) );
		_tapReady = (TimeUntil)DryTapRefireDelay;
	}

	void FirePrimaryRound()
	{
		if ( Secondary == SecondaryFires.DoubleShot && HasOwner && !NpcControlled )
		{
			if ( !TakePrimaryAmmo() ) return;
			ShootEffects( FireBuckshot( Ballistics.Pellets ) );
		}
		else base.PrimaryAttack();
		if ( Secondary == SecondaryFires.DoubleShot && WeaponSetup.PumpDelay( this ) is { } delay ) _pumpStarts = delay;
		if ( AccuracyPenaltyPerShot > 0 && HasOwner ) _accuracyPenalty += AccuracyPenaltyPerShot;
		if ( Secondary == SecondaryFires.DoubleShot || BlocksUnusedSecondaryUnderwater ) SetNextFire( PrimaryDelay );
		if ( TapRefireDelay > 0 && HasOwner ) _tapReady = (TimeUntil)TapRefireDelay;
		if ( HasOwner && MachineGunKick.y > 0 )
		{
			// Rounds that fall due together are one machine gun volley with a single view kick.
			if ( !Owner.ThirdPerson && !Owner.IsProxy && _lastMachineGunKickTime != Time.NowDouble )
			{
				_lastMachineGunKickTime = Time.NowDouble;
				ViewPunch.MachineGunKick( Owner, MachineGunKick.x * Math.Min( _fireDuration / MachineGunKick.y, 1 ) );
			}
		}
		else DoRecoil();
		if ( !BurstMode || NpcControlled ) return;
		var burst = Css.Rules.Burst( WeaponSetup.WeaponType( this ) );
		_burstRemaining = Css.Rules.BurstShots - 1;
		_burstAttacker = Attacker;
		_burstAt = burst.Interval;
		SetNextFire( burst.Cycle );
	}

	void UpdateBurst()
	{
		if ( _burstRemaining <= 0 || _burstAt > 0 ) return;
		_burstRemaining--;
		_burstAt = Css.Rules.Burst( WeaponSetup.WeaponType( this ) ).Interval;
		if ( !HasPrimaryAmmo() || IsReloading )
		{
			CancelBurst();
			return;
		}
		BurstFollowUp = true;
		try
		{
			base.PrimaryAttack();
			DoRecoil();
		}
		finally
		{
			BurstFollowUp = false;
			if ( _burstRemaining <= 0 ) _burstAttacker = null;
		}
	}

	void CancelBurst()
	{
		_burstRemaining = 0;
		_burstAttacker = null;
	}

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		CancelBurst();
		base.OnAdded( inventory );
	}

	protected override async void OnStart()
	{
		base.OnStart();
		try
		{
			await WeaponSetup.ConfigureWeapon( this );
			if ( !IsValid ) return;
			if ( Networking.IsHost && NpcControlled && UsesPrimaryClip )
				Clip1 = ClipMaxSize;
			_weaponConfigured = true;
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't configure mounted weapon: {e.Message}" );
		}
	}

	protected override void OnReloadInserted()
	{
		if ( Secondary == SecondaryFires.DoubleShot && !NpcControlled )
		{
			SetNextFire( ReloadTime );
			_lastShellInserting = Clip1 >= PrimaryClipSize || Ammo1 == 0;
		}
		base.OnReloadInserted();
		if ( WeaponModel is not Source1.ViewModel ) Sounds.PlayWeaponSound( this, "reload" );
	}

	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		base.ModifyCamera( camera, ref view );
		if ( _zoomLevel <= 0 ) return;
		if ( ZoomLevels is not { Count: > 0 } levels )
		{
			view.FieldOfView = Zoom.FieldOfView;
			return;
		}
		// Counter-Strike's zoom FOVs are relative to its default of 90: keep that magnification from the player's own FOV.
		var magnification = MathF.Tan( 45f.DegreeToRadian() ) / MathF.Tan( (levels[_zoomLevel - 1] * 0.5f).DegreeToRadian() );
		view.FieldOfView = (MathF.Atan( MathF.Tan( (view.FieldOfView * 0.5f).DegreeToRadian() ) / magnification ) * 2).RadianToDegree();
	}

	public override void DrawHud( Painter painter, Vector2 crosshair )
	{
		if ( _scoped && Scene.Camera.IsValid() ) Scope.Draw( painter, Scene.Camera.ScreenRect.Size );
		base.DrawHud( painter, crosshair );
	}

	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		if ( _scoped ) return;
		Crosshair.Draw( painter, center, CanPrimaryAttack(), SpreadBloom );
	}

	/// <summary>Sniper rifles look through the scope overlay with the viewmodel hidden, as Counter-Strike does.</summary>
	void SetZoom( int level )
	{
		_zoomLevel = level;
		_scoped = level > 0 && WeaponSetup.WeaponType( this ) == "SniperRifle" && HasOwner && !Owner.ThirdPerson;
		if ( ViewModel.IsValid() ) ViewModel.Tags.Set( "sniper_scoped", _scoped );
	}

	public override bool HasSecondaryAmmo() => Secondary switch
	{
		SecondaryFires.DoubleShot => HasPrimaryAmmo() && (!IsReloading || Clip1 >= 2),
		SecondaryFires.Zoom or SecondaryFires.Burst or SecondaryFires.Silencer => true,
		_ => base.HasSecondaryAmmo()
	};

	internal override bool ShouldPlayIdle => !UsesPrimaryClip || IncrementalReloading || !AttackOrReloadHeld;

	public override bool CanSecondaryAttack() => !_alternatePending && _burstRemaining <= 0
		&& (Launches || Secondary is SecondaryFires.DoubleShot or SecondaryFires.Zoom or SecondaryFires.Burst or SecondaryFires.Silencer)
		&& (IsReloading && Launches && CanLaunchDuringReload ? NextSecondaryFire <= 0 && HasSecondaryAmmo() : base.CanSecondaryAttack());

	public override void SecondaryAttack()
	{
		if ( _alternatePending || !HasSecondaryAmmo() ) return;
		if ( Secondary == SecondaryFires.Zoom )
		{
			SetNextSecondaryFire( Css.Rules.ToggleDelay );
			var steps = ZoomLevels is { Count: > 0 } ? ZoomLevels.Count : 1;
			SetZoom( (_zoomLevel + 1) % (steps + 1) );
			if ( Sounds.WeaponSound( this, "special3" ) is { } zoomSound ) GameObject.PlaySound( zoomSound );
			return;
		}
		if ( Secondary == SecondaryFires.Burst )
		{
			SetNextSecondaryFire( Css.Rules.ToggleDelay );
			BurstMode = !BurstMode;
			return;
		}
		if ( Secondary == SecondaryFires.Silencer )
		{
			SetNextFire( Math.Max( SilencerDuration, Css.Rules.ToggleDelay ) );
			Silenced = !Silenced;
			SilencerEffects( Silenced );
			return;
		}
		if ( Secondary == SecondaryFires.DoubleShot )
		{
			DoubleShot();
			return;
		}
		if ( !FiresUnderwater && BlockSubmergedShot( blockBothTriggers: true ) ) return;
		if ( IsReloading && CanLaunchDuringReload ) CancelReload();
		if ( AlternateProjectile == Projectile.Kinds.EnergyBall )
		{
			_alternatePending = true;
			_alternateAttacker = Attacker;
			_alternateAt = EnergyBallWindup;
			SetNextFire( EnergyBallWindup );
			ChargeEffects();
			return;
		}
		ReleaseAlternate( Attacker );
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void PumpEffects()
	{
		if ( WeaponModel is ViewModel view ) view.PlayPump();
		else if ( WeaponModel is WeaponWorldModel world ) world.PlayPump();
	}

	void DoubleShot()
	{
		_queuedSecondary = false;
		if ( IsHeld && Clip1 == 1 )
		{
			FirePrimaryRound();
			return;
		}
		if ( !FiresUnderwater && BlockSubmergedShot( blockBothTriggers: true ) ) return;
		if ( IsHeld && !TakePrimaryAmmo( 2 ) ) return;
		var volley = FireBuckshot( DoubleShotPellets );
		_pumpStarts = AlternatePumpDelay;
		SetNextFire( AlternatePumpDelay + PumpDuration );
		AlternateEffects( volley );
		DoRecoil( new Angles( Random.Shared.Float( -5, 5 ), 0, 0 ) );
	}

	// The engine batches hit claims and calls BulletTrace once per pellet, in firing order.
	(int Count, int Index, Vector3 Right, Vector3 Up) _buckshot;

	ShotEffect[] FireBuckshot( int count )
	{
		var config = Ballistics;
		var spread = CurrentSpread;
		var previous = _buckshot;
		try
		{
			if ( HasOwner && !NpcControlled )
			{
				var aim = Rotation.LookAt( AimRay.Forward );
				_buckshot = (count, 0, aim.Right * MathF.Sin( (spread.x * 0.5f).DegreeToRadian() ),
					aim.Up * MathF.Sin( (spread.y * 0.5f).DegreeToRadian() ));
				spread = Vector2.Zero;
			}
			var hits = ShootBullets( count, spread, config.Range, config.Radius, config.Damage, config.Force );
			TimeSinceShoot = 0;
			var effects = new ShotEffect[hits.Length];
			for ( int i = 0; i < hits.Length; i++ )
			{
				var tr = hits[i];
				effects[i] = new ShotEffect( tr.EndPosition, tr.Hit, tr.Normal, tr.GameObject, tr.Surface, NoEvents: i > 0 );
			}
			return effects;
		}
		finally { _buckshot = previous; }
	}

	protected override SceneTrace BulletTrace( Ray ray, float distance, float radius )
	{
		if ( _buckshot.Count == 0 ) return base.BulletTrace( ray, distance, radius );

		Vector2 offset;
		do
		{
			offset = new Vector2( Random.Shared.Float( -0.5f, 0.5f ) + Random.Shared.Float( -0.5f, 0.5f ),
				Random.Shared.Float( -0.5f, 0.5f ) + Random.Shared.Float( -0.5f, 0.5f ) );
		} while ( offset.LengthSquared > 1 );

		ray.Forward = (ray.Forward + _buckshot.Right * offset.x + _buckshot.Up * offset.y).Normal;
		var trace = base.BulletTrace( ray, distance, 0 );
		return (_buckshot.Index++ & 1) == 0 ? trace : trace.Size( Vector3.One * 6 );
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void SilencerEffects( bool silenced ) => (WeaponModel as ViewModel)?.OnSilencer( silenced );

	void OnSilencedChanged( bool previous, bool silenced )
	{
		if ( Sounds.WeaponSound( this, Sounds.FireSoundRole( this ) ) is { } fire ) AttackSound = fire;
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void ChargeEffects()
	{
		(WeaponModel as ViewModel)?.OnSecondaryWindup();
		Sounds.PlayWeaponSound( this, "special1" );
	}

	void ReleaseAlternate( GameObject attacker )
	{
		// SecondaryAttack checked ammo at trigger time. A charged shot still releases if its reserve was drained.
		if ( IsHeld ) TakeSecondaryAmmo( 1 );
		TimeSinceShoot = 0;
		SetNextPrimaryFire( PrimaryDelayAfterLaunch );
		SetNextSecondaryFire( SecondaryDelay );
		AlternateEffects( [new ShotEffect()] );
		var aim = AimRay;
		var direction = AlternateProjectile == Projectile.Kinds.ContactGrenade && HasOwner && !Owner.IsProxy && !Owner.ThirdPerson
			? ViewPunch.AimDirection( Scene.Camera, aim.Forward ) : aim.Forward;
		LaunchAlternate( aim.Position, direction, attacker );
		if ( AlternateProjectile == Projectile.Kinds.EnergyBall && HasOwner && !Owner.IsProxy )
		{
			if ( !Owner.ThirdPerson )
				ViewPunch.Apply( Owner, new Angles( -8, Random.Shared.Next( 1, 3 ), 0 ), false );
			ScreenFade.Show( Scene.Camera, Color.White.WithAlpha( 64 / 255f ), 0, 0.1f );
		}
	}

	/// <summary>
	/// Alternate-fire presentation: every pellet's impact and tracer, then the script's double_shot
	/// sound and the model's alternate attack in place of the primary fire events.
	/// </summary>
	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void AlternateEffects( ShotEffect[] volley )
	{
		for ( int i = 0; i < volley.Length; i++ )
			PlayPelletEffects( volley[i] with { NoEvents = true }, volley.Length, i == 0 );
		PostWeaponEvent( WeaponEvents.Attack );
		AlternateNoise();
		if ( Application.IsDedicatedServer ) return;
		Sounds.PlayWeaponSound( this, "double_shot" );
		if ( WeaponModel is ViewModel view ) view.OnAlternateAttack();
		else if ( WeaponModel is WeaponWorldModel world ) world.OnDoubleShot( volley[0].Hit ? volley[0].HitPosition : null );
		else WeaponModel?.OnAttack( volley[0].Hit ? volley[0].HitPosition : null );
	}

	/// <summary>Alternate fire is gunfire too: NPCs hear it as player gunshots, 3000 units for 1.5 seconds.</summary>
	void AlternateNoise()
	{
		if ( !Networking.IsHost || !Owner.IsValid() ) return;
		Scene.GetSystem<Sandbox.Npcs.NpcSystem>()?.EmitStimulus( Sandbox.Npcs.StimulusKinds.Gunshot, Owner.WorldPosition, Owner.GameObject, radius: 3000f, lifetime: 1.5f );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void LaunchAlternate( Vector3 start, Vector3 direction, GameObject attacker )
	{
		if ( AlternateProjectile is not { } kind ) return;
		Projectile.Launch( this, ResolveLaunchAttacker( attacker ), kind, start, direction * AlternateSpeed, AlternateDamage ?? DefaultAlternateDamage, AlternateRadius ?? DefaultAlternateRadius );
	}

	/// <summary>
	/// The per-shot kick: eye punch and camera shake for a held gun, a physical shove for a standalone one.
	/// </summary>
	void DoRecoil( Angles? punch = null )
	{
		if ( !HasOwner )
		{
			if ( ShootForce > 0f && GetComponent<Rigidbody>( true ) is { } rb )
			{
				var muzzle = WeaponModel?.MuzzleGameObject?.WorldTransform ?? WorldTransform;
				rb.ApplyForce( muzzle.Rotation.Up * ShootForce );
			}

			return;
		}

		KickView( punch );
	}

	/// <summary>Runs for every pellet on every peer: NPCs hear the shot, and ragdolls take the hit wherever they simulate.</summary>
	protected override void OnShootEffects( ShotEffect shot ) =>
		PlayPelletEffects( shot, Ballistics.Pellets, !shot.NoEvents );

	void PlayPelletEffects( ShotEffect shot, int pelletCount, bool first )
	{
		base.OnShootEffects( shot );

		DrawPelletTracer( shot, pelletCount, first );
		EmitGunfireNoise( shot );

		if ( shot.Hit )
			PushRagdoll( shot.HitObject, shot.HitPosition, shot.Origin ?? GetMuzzleTransform().Position, RagdollImpactScale, RagdollBodyShare );
	}

	void DrawPelletTracer( in ShotEffect shot, int pelletCount, bool first )
	{
		if ( Application.IsDedicatedServer || TracerFrequency <= 0 || (!shot.Hit && shot.HitPosition == default) ) return;
		if ( Secondary != SecondaryFires.DoubleShot )
		{
			if ( !first ) return;
			pelletCount = 1;
		}

		var phase = _tracerShots++ % (uint)TracerFrequency;
		// Remote player fire carries one tracer flag for the entire volley.
		if ( first ) _remoteVolleyTracer = phase == 0 || pelletCount > TracerFrequency - phase;
		if ( IsProxy && HasOwner ? !_remoteVolleyTracer : phase != 0 ) return;

		if ( WeaponModel is { } model && model.IsValid() )
			WeaponEffects.DrawTracer( Scene, WeaponSetup.FlashType( this ), model.GetTracerOrigin().Position, shot.HitPosition, MountContent.OfWeapon( this ) );
	}

	protected override void OnShootImpact( in ShotEffect shot )
	{
		if ( !shot.Hit ) return;
		var mount = MountContent.OfWeapon( this );
		if ( WeaponSetup.WeaponData( this )?.ClassName == "weapon_ar2" && !(shot.HitObject.IsValid() && shot.HitObject.Tags.Has( "source1_sky" )) )
			Hl2.FlatQuad.Create( "effects/combinemuzzle2", shot.HitPosition + shot.Normal, shot.Normal, Game.Random.Float( 24, 32 ), 0, 0.25f, mount );

		if ( !WeaponEffects.Impact( shot, GetShotDirection( shot ), mount ) ) base.OnShootImpact( shot );
	}
}
