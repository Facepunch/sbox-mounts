namespace Halo2;

public class MagazineWeapon : HandheldWeapon
{
	[Property] public int BurstLength { get; set; }
	[Property] public float RecoveryDelay { get; set; }
	[Property] public bool HasIdleMagazineBarrel { get; set; }
	int remainingShots;
	double burstRoundAt;
	GameObject burstAttacker;
	double? transferAt;
	double holsterTransferAt;
	bool transferred;
	double? emptyRoundAt;
	double barrelIdleAt;
	bool dryFiring;
	bool triggerPressedDuringReload;

	protected override bool CanCycleBarrel => BurstLength <= 1 && base.CanCycleBarrel;
	protected virtual bool PredictProjectileEffects => true;
	protected override bool WantsPrimaryAttack() => !IsReloading && (triggerPressedDuringReload || base.WantsPrimaryAttack());

	public override void PrimaryAttack()
	{
		triggerPressedDuringReload = false;
		double roundAt = RecordRound();
		emptyRoundAt = null;
		if ( PrimaryAutomatic && BurstLength <= 1 && !HasIdleMagazineBarrel && IsHeld && Clip1 == 1 )
		{
			emptyRoundAt = roundAt;
			// A held trigger attempts one dry shot before the barrel enters recovery.
			double heldDelay = Holder is PlayerController && Input.Down( "attack1" ) ? PrimaryDelay : 0;
			barrelIdleAt = roundAt + heldDelay + 1 / 30.0 + RecoveryDelay;
		}
		if ( BurstLength > 1 && remainingShots == 0 )
		{
			remainingShots = BurstLength;
			burstRoundAt = Time.NowDouble;
			burstAttacker = Attacker;
		}
		if ( Instantaneous ) base.PrimaryAttack();
		else if ( Networking.IsHost ) FireRound( AimRay.Forward );
		else
		{
			if ( PredictProjectileEffects )
			{
				OnShootEffects( default );
				TimeSinceShoot = 0;
			}
			else SetNextPrimaryFire( 0 ); // The host starts the cooldown when this trigger actually fires.
			RequestRound( AimRay.Forward );
		}
		if ( remainingShots == 0 ) return;
		if ( --remainingShots == 0 || (IsHeld && !HasPrimaryAmmo()) )
		{
			// An incomplete burst enters recovery when its next round would have fired.
			burstRoundAt += RecoveryDelay + (remainingShots > 0 ? PrimaryDelay : 0);
			remainingShots = 0;
			burstAttacker = null;
		}
		else
		{
			burstRoundAt += PrimaryDelay;
		}
		SetNextPrimaryFire( (float)System.Math.Max( 0, burstRoundAt - Time.NowDouble ) );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestRound( Vector3 forward )
	{
		if ( !IsActive || !IsHeld || !CanPrimaryAttack() || !forward.LengthSquared.AlmostEqual( 1, 0.001f ) ) return;
		SetNextPrimaryFire( PrimaryDelay );
		FireRound( forward );
	}

	protected virtual void FireRound( Vector3 forward )
	{
		if ( IsHeld && !TakePrimaryAmmo() ) return;
		LaunchBolt( forward );
		TimeSinceShoot = 0;
		ShootEffects();
	}

	public override bool CanReload() => remainingShots == 0 && !(dryFiring && emptyRoundAt.HasValue) && base.CanReload();

	protected override void OnControl()
	{
		bool held = Input.Down( "attack1" );
		if ( !held ) triggerPressedDuringReload = false;
		else if ( !PrimaryAutomatic && IsReloading && Input.Pressed( "attack1" ) ) triggerPressedDuringReload = true;
		if ( emptyRoundAt is double shot && !held )
		{
			double elapsedTicks = System.Math.Max( 1, System.Math.Floor( (Time.NowDouble - shot) * 30 ) );
			barrelIdleAt = System.Math.Min( barrelIdleAt, shot + elapsedTicks / 30 + RecoveryDelay );
		}
		base.OnControl();
	}

	public override void DryFire()
	{
		dryFiring = true;
		try { base.DryFire(); }
		finally { dryFiring = false; }
	}

	public override void Reload() => BeginReload( automatic: false );

	[Rpc.Host( NetFlags.OwnerOnly )]
	void ReloadEmptyBarrel()
	{
		if ( IsHeld && IsActive && AutoReload && Clip1 == 0 ) BeginReload( automatic: true );
	}

	void BeginReload( bool automatic )
	{
		if ( Networking.IsHost && CanReload() )
		{
			emptyRoundAt = null;
			transferAt = null;
			transferred = false;
			if ( FirstPersonAnimations()?.Actions.TryGetValue( Clip1 == 0 ? "reload_empty" : "reload_full", out var clip ) == true )
			{
				// Auto-reload is requested after the current tick's magazine update.
				int startTick = automatic ? 1 : 0;
				ReloadTime = (clip.Frames + startTick) / 30f;
				if ( clip.Frames > 0 )
				{
					var cue = clip.Cues.FirstOrDefault( cue => cue.Kind == "primary_keyframe" );
					int ticks = cue?.Frame is int key && key >= 0 && key <= clip.Frames ? key : clip.Frames;
					transferAt = Time.NowDouble + (System.Math.Max( 0, ticks - 1 ) + startTick) / 30.0;
					// Holstering transfers the rounds once fewer than half the transfer ticks remain.
					holsterTransferAt = Time.NowDouble + (ticks / 2 + 1 + startTick) / 30.0;
				}
			}
		}
		base.Reload();
	}

	protected override int TakeReserveAmmo( BaseAmmoResource ammoType, int amount ) =>
		IsReloading && transferred && ammoType == PrimaryAmmoType ? 0 : base.TakeReserveAmmo( ammoType, amount );

	void TransferMagazine()
	{
		transferAt = null;
		Clip1 += TakeReserveAmmo( PrimaryAmmoType, ReloadFillTarget - Clip1 );
		transferred = true;
	}

	protected override bool OnHolstering( BaseInventoryItem next )
	{
		if ( Networking.IsHost && IsReloading && transferAt.HasValue && Time.NowDouble >= holsterTransferAt )
			TransferMagazine();
		return base.OnHolstering( next );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( IsReloading && transferAt is double due && Time.NowDouble >= due )
			TransferMagazine();
		if ( remainingShots > 0 && NextPrimaryFire <= 0 )
			FirePrimary( burstAttacker );
		if ( IsReloading ) emptyRoundAt = null;
		if ( IsProxy || !IsActive || !IsHeld || !AutoReload || Clip1 != 0 || !CanReload() ) return;
		if ( emptyRoundAt.HasValue ? Time.NowDouble >= barrelIdleAt : HasIdleMagazineBarrel || NextPrimaryFire <= 0 )
			ReloadEmptyBarrel();
	}

	protected override void OnDisabled()
	{
		triggerPressedDuringReload = false;
		remainingShots = 0;
		emptyRoundAt = null;
		burstAttacker = null;
		base.OnDisabled();
	}

	public override bool CanSecondaryAttack() => remainingShots == 0 && base.CanSecondaryAttack();
}
