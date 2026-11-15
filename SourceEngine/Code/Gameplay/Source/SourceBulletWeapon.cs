using System;
using Sandbox.Npcs;

namespace Source1;

static class SourceZoom
{
	/// <summary>weapon_crossbow zooms the player to a 20 degree field of view.</summary>
	public const float FieldOfView = 20;
}

/// <summary>The sandbox rifles' four arms, opening with spread bloom; Source weapons draw no crosshair of their own.</summary>
static class SourceCrosshair
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
static class SourceScope
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
public sealed class SourceBulletWeapon : SourceWeapon
{
	/// <summary>Physical kick applied to a standalone (unheld) gun when it fires.</summary>
	[Property, Group( "Recoil" )] public float ShootForce { get; set; } = 100000f;

	/// <summary>
	/// Ragdolls take the bullet as a velocity change: the struck bone gains <c>Ballistics.Force * RagdollImpactScale</c>
	/// units/sec along the shot; every other bone gains <see cref="RagdollBodyShare"/> of that.
	/// </summary>
	[Property, Group( "Ragdoll Impact" ), Range( 0f, 10f )] public float RagdollImpactScale { get; set; } = 1f;

	/// <summary>Fraction of the struck bone's velocity change given to the rest of the ragdoll.</summary>
	[Property, Group( "Ragdoll Impact" ), Range( 0f, 1f )] public float RagdollBodyShare { get; set; } = 0.3f;

	// weapon_ar2 and weapon_smg1 launch their alt fire at 1000 units/s; the damage and radius
	// fall back to the SDK's sk_plr_dmg_smg1_grenade and sk_smg1_grenade_radius.
	const float AlternateSpeed = 1000;
	const float DefaultAlternateDamage = 100;
	const float DefaultAlternateRadius = 250;
	// weapon_ar2 DelayedAttack fires the ball half a second after the charge starts.
	const float EnergyBallWindup = 0.5f;
	// weapon_shotgun SecondaryAttack fires a literal 12 pellets.
	const int DoubleShotPellets = 12;

	[Property] public SecondaryFires Secondary { get; set; }
	/// <summary>Field of view of each <see cref="SecondaryFires.Zoom"/> step; empty zooms once to <see cref="SourceZoom.FieldOfView"/>.</summary>
	[Property] public List<float> ZoomLevels { get; set; }
	[Sync] public bool BurstMode { get; set; }
	[Sync, Change( nameof( OnSilencedChanged ) )] public bool Silenced { get; set; }
	internal float SilencerDuration { get; set; }
	/// <summary>True while firing the second and later rounds of a burst.</summary>
	internal bool BurstFollowUp { get; private set; }
	[Sync( SyncFlags.FromHost )] bool AlternateReserveSeeded { get; set; }
	internal float? AlternateDamage { get; set; }
	internal float? AlternateRadius { get; set; }
	internal SourceProjectile.Kinds? AlternateProjectile { get; set; }
	internal float AlternateDuration { get; set; } = 1;
	internal bool NpcControlled => Holder is BaseNpc;
	// The host picks the NPC's reload clip and shares its duration for animation on other peers.
	[Sync( SyncFlags.FromHost )] internal float NpcReloadAnimationTime { get; private set; }
	(float Duration, bool Incremental)? _playerReloadSettings;
	bool _weaponConfigured;
	bool? _tunedForNpc;
	bool _alternatePending;
	TimeUntil _alternateAt;
	GameObject _alternateAttacker;
	int _zoomLevel;
	int _burstRemaining;
	TimeUntil _burstAt;
	GameObject _burstAttacker;
	bool _scoped;

	bool Launches => Secondary == SecondaryFires.Default && AlternateProjectile is not null;

	protected override GameObject Attacker => BurstFollowUp && _burstAttacker.IsValid() ? _burstAttacker : base.Attacker;

	public override bool CanPrimaryAttack() => !_alternatePending && _burstRemaining <= 0
		&& (!NpcControlled || _weaponConfigured) && base.CanPrimaryAttack();

	public override bool CanReload() => !_alternatePending && (!NpcControlled || _weaponConfigured) && base.CanReload();

	protected override void OnHolstered()
	{
		_alternatePending = false;
		_alternateAttacker = null;
		CancelBurst();
		SetZoom( 0 );
		base.OnHolstered();
	}

	protected override void OnDisabled()
	{
		CancelBurst();
		SetZoom( 0 );
		base.OnDisabled();
	}

	protected override void OnReloadStarted()
	{
		SetZoom( 0 );
		CancelBurst();
		var animator = GetComponentInParent<SourceNpcAnimator>();
		if ( Networking.IsHost && NpcControlled )
		{
			// The engine reads these settings after this callback. NPCs fill the magazine
			// when their animation ends, including when the animation loops for each shell.
			_playerReloadSettings = (ReloadTime, IncrementalReloading);
			var duration = animator.IsValid() ? animator.ReloadDuration( ClipMaxSize - Clip1 ) : null;
			NpcReloadAnimationTime = ReloadTime = Math.Max( duration ?? ReloadTime, 0.1f );
			IncrementalReloading = false;
		}
		base.OnReloadStarted();
		if ( !animator.IsValid() && WeaponModel is not SourceViewModel ) SourceSounds.PlayReloadSound( this, GameObject );
	}

	protected override void OnReloadFinished()
	{
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
		var npc = NpcControlled;
		if ( npc != _tunedForNpc && SourceWeaponSetup.ApplyHolder( this, npc ) ) _tunedForNpc = npc;
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
		base.PrimaryAttack();
		DoRecoil();
		if ( !BurstMode || NpcControlled ) return;
		var burst = CssRules.Burst( SourceWeaponSetup.WeaponType( this ) );
		_burstRemaining = CssRules.BurstShots - 1;
		_burstAttacker = Attacker;
		_burstAt = burst.Interval;
		SetNextFire( burst.Cycle );
	}

	void UpdateBurst()
	{
		if ( _burstRemaining <= 0 || _burstAt > 0 ) return;
		_burstRemaining--;
		_burstAt = CssRules.Burst( SourceWeaponSetup.WeaponType( this ) ).Interval;
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

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		SourceWeaponSetup.ConfigureAmmo( this );
		return base.OnAdding( inventory );
	}

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		CancelBurst();
		base.OnAdded( inventory );
		if ( AlternateReserveSeeded ) return;
		AlternateReserveSeeded = true;
		if ( SecondaryAmmoType is not null && !inventory.HasAmmo( SecondaryAmmoType ) )
			inventory.GiveAmmo( SecondaryAmmoType, SecondaryAmmoType.MaxReserve );
	}

	protected override async void OnStart()
	{
		base.OnStart();
		try
		{
			await SourceWeaponSetup.ConfigureWeapon( this );
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
		base.OnReloadInserted();
		if ( WeaponModel is not SourceViewModel ) SourceSounds.PlayReloadSound( this, GameObject );
	}

	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		base.ModifyCamera( camera, ref view );
		if ( _zoomLevel <= 0 ) return;
		if ( ZoomLevels is not { Count: > 0 } levels )
		{
			view.FieldOfView = SourceZoom.FieldOfView;
			return;
		}
		// Counter-Strike's zoom FOVs are relative to its default of 90: keep that magnification from the player's own FOV.
		var magnification = MathF.Tan( 45f.DegreeToRadian() ) / MathF.Tan( (levels[_zoomLevel - 1] * 0.5f).DegreeToRadian() );
		view.FieldOfView = (MathF.Atan( MathF.Tan( (view.FieldOfView * 0.5f).DegreeToRadian() ) / magnification ) * 2).RadianToDegree();
	}

	public override void DrawHud( Painter painter, Vector2 crosshair )
	{
		if ( _scoped && Scene.Camera.IsValid() ) SourceScope.Draw( painter, Scene.Camera.ScreenRect.Size );
		base.DrawHud( painter, crosshair );
	}

	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		if ( _scoped ) return;
		SourceCrosshair.Draw( painter, center, CanPrimaryAttack(), SpreadBloom );
	}

	/// <summary>Sniper rifles look through the scope overlay with the viewmodel hidden, as Counter-Strike does.</summary>
	void SetZoom( int level )
	{
		_zoomLevel = level;
		_scoped = level > 0 && SourceWeaponSetup.WeaponType( this ) == "SniperRifle" && HasOwner && !Owner.ThirdPerson;
		if ( ViewModel.IsValid() ) ViewModel.Tags.Set( "sniper_scoped", _scoped );
	}

	public override bool HasSecondaryAmmo() => Secondary switch
	{
		SecondaryFires.DoubleShot => !IsHeld || Clip1 >= 2,
		SecondaryFires.Zoom or SecondaryFires.Burst or SecondaryFires.Silencer => true,
		_ => base.HasSecondaryAmmo()
	};

	public override bool CanSecondaryAttack() => !_alternatePending && _burstRemaining <= 0
		&& (Launches || Secondary is SecondaryFires.DoubleShot or SecondaryFires.Zoom or SecondaryFires.Burst or SecondaryFires.Silencer)
		&& base.CanSecondaryAttack();

	public override void SecondaryAttack()
	{
		if ( _alternatePending || !HasSecondaryAmmo() ) return;
		if ( Secondary == SecondaryFires.Zoom )
		{
			SetNextSecondaryFire( CssRules.ToggleDelay );
			var steps = ZoomLevels is { Count: > 0 } ? ZoomLevels.Count : 1;
			SetZoom( (_zoomLevel + 1) % (steps + 1) );
			if ( SourceSounds.WeaponSound( this, "special3" ) is { } zoomSound ) GameObject.PlaySound( zoomSound );
			return;
		}
		if ( Secondary == SecondaryFires.Burst )
		{
			SetNextSecondaryFire( CssRules.ToggleDelay );
			BurstMode = !BurstMode;
			return;
		}
		if ( Secondary == SecondaryFires.Silencer )
		{
			SetNextFire( Math.Max( SilencerDuration, CssRules.ToggleDelay ) );
			Silenced = !Silenced;
			SilencerEffects( Silenced );
			return;
		}
		if ( Secondary == SecondaryFires.DoubleShot )
		{
			DoubleShot();
			return;
		}
		if ( AlternateProjectile == SourceProjectile.Kinds.EnergyBall )
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

	void DoubleShot()
	{
		if ( IsHeld && !TakePrimaryAmmo( 2 ) ) return;
		var config = Ballistics;
		var hits = ShootBullets( DoubleShotPellets, CurrentSpread, config.Range, config.Radius, config.Damage, config.Force );
		var volley = hits.Select( tr => new ShotEffect( tr.EndPosition, tr.Hit, tr.Normal, tr.GameObject, tr.Surface ) ).ToArray();
		TimeSinceShoot = 0;
		SetNextFire( AlternateDuration );
		AlternateEffects( volley );
		DoRecoil();
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void SilencerEffects( bool silenced ) => (WeaponModel as SourceViewModel)?.OnSilencer( silenced );

	void OnSilencedChanged( bool previous, bool silenced )
	{
		if ( SourceSounds.WeaponSound( this, SourceSounds.FireSoundRole( this ) ) is { } fire ) AttackSound = fire;
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void ChargeEffects()
	{
		(WeaponModel as SourceViewModel)?.OnSecondaryWindup();
		if ( SourceSounds.WeaponSound( this, "special1" ) is { } sound ) GameObject.PlaySound( sound );
	}

	void ReleaseAlternate( GameObject attacker )
	{
		if ( IsHeld && !TakeSecondaryAmmo( 1 ) ) return;
		TimeSinceShoot = 0;
		SetNextFire( AlternateDuration );
		AlternateEffects( [new ShotEffect()] );
		KickView();
		LaunchAlternate( AimRay.Position, AimRay.Forward, attacker );
	}

	/// <summary>
	/// Alternate-fire presentation: every pellet's impact and tracer, then the script's double_shot
	/// sound and the model's alternate attack in place of the primary fire events.
	/// </summary>
	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void AlternateEffects( ShotEffect[] volley )
	{
		foreach ( var shot in volley ) OnShootEffects( shot with { NoEvents = true } );
		PostWeaponEvent( WeaponEvents.Attack );
		AlternateNoise();
		if ( Application.IsDedicatedServer ) return;
		if ( SourceSounds.WeaponSound( this, "double_shot" ) is { } sound ) GameObject.PlaySound( sound );
		if ( WeaponModel is SourceViewModel view ) view.OnAlternateAttack();
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
		SourceProjectile.Launch( this, ResolveLaunchAttacker( attacker ), kind, start, direction * AlternateSpeed, AlternateDamage ?? DefaultAlternateDamage, AlternateRadius ?? DefaultAlternateRadius );
	}

	/// <summary>
	/// The per-shot kick: eye punch and camera shake for a held gun, a physical shove for a standalone one.
	/// </summary>
	void DoRecoil()
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

		KickView();
	}

	/// <summary>Runs for every pellet on every peer: NPCs hear the shot, and ragdolls take the hit wherever they simulate.</summary>
	protected override void OnShootEffects( ShotEffect shot )
	{
		base.OnShootEffects( shot );

		EmitGunfireNoise( shot );

		if ( shot.Hit )
			PushRagdoll( shot.HitObject, shot.HitPosition, shot.Origin ?? GetMuzzleTransform().Position, RagdollImpactScale, RagdollBodyShare );
	}

	protected override void OnShootImpact( in ShotEffect shot )
	{
		if ( !SourceWeaponEffects.Impact( shot, SourceMount.OfWeapon( this ) ) ) base.OnShootImpact( shot );
	}
}
