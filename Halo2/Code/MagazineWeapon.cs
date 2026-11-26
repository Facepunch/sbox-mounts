namespace Halo2;

public class MagazineWeapon : Firearm
{
	[Property] public int BurstLength { get; set; }
	[Property] public float RecoveryDelay { get; set; }
	[Property] public int RecoveryBufferTicks { get; set; }
	[Property] public bool HasIdleMagazineBarrel { get; set; }
	int remainingShots;
	double burstStartTick;
	double burstRoundAt;
	GameObject burstAttacker;
	double? transferAt;
	double holsterTransferAt;
	bool transferred;
	double? emptyRoundAt;
	double barrelIdleAt;
	bool dryFiring;
	bool triggerPressedDuringReload;
	bool triggerPressedDuringRecovery;
	double recoveryEndsAt;

	protected override bool CanCycleBarrel => BurstLength <= 1 && base.CanCycleBarrel;
	protected override bool HasPendingRound => remainingShots > 0 || base.HasPendingRound;
	protected virtual bool PredictProjectileEffects => true;
	protected override bool WantsPrimaryAttack() => remainingShots == 0 && !IsReloading && (triggerPressedDuringRecovery || triggerPressedDuringReload || base.WantsPrimaryAttack());

	public override void PrimaryAttack()
	{
		triggerPressedDuringReload = false;
		triggerPressedDuringRecovery = false;
		double roundAt = RecordRound();
		if ( RecoveryBufferTicks > 0 )
		{
			recoveryEndsAt = System.Math.Floor( roundAt * 30 ) / 30 + PrimaryDelay;
			SetNextPrimaryFire( (float)(recoveryEndsAt - Time.NowDouble) );
		}
		RecordEmptyRound( roundAt, Holder is PlayerController && Input.Down( TriggerButton ) );
		if ( BurstLength > 1 && remainingShots == 0 )
		{
			remainingShots = BurstLength;
			burstStartTick = System.Math.Floor( roundAt * 30 );
			burstRoundAt = burstStartTick / 30;
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
			RequestRound( AimRay.Forward, false, FiringError, (long)System.Math.Round( roundAt * 30 ) );
		}
		if ( remainingShots == 0 ) return;
		if ( --remainingShots == 0 || (IsHeld && !HasPrimaryAmmo()) )
		{
			// An incomplete burst enters recovery when its next round would have fired.
			burstRoundAt += RecoveryDelay + (remainingShots > 0 ? PrimaryDelay : 0);
			// The magazine sees the idle barrel on the tick after burst recovery ends.
			barrelIdleAt = burstRoundAt + 1 / 30.0;
			remainingShots = 0;
			burstAttacker = null;
		}
		else
		{
			burstRoundAt += PrimaryDelay;
		}
		SetNextPrimaryFire( (float)System.Math.Max( 0, burstRoundAt - Time.NowDouble ) );
	}

	protected override bool UsesRemoteAutomaticFire => !Instantaneous && PredictProjectileEffects && BurstLength <= 1 && PrimaryAutomatic;
	protected override void FireRemoteRound( Vector3 forward, bool awaitingCue, double shotTime )
	{
		if ( Instantaneous ) return;
		if ( UsesRemoteAutomaticFire ) RecordEmptyRound( shotTime, true );
		FireRound( forward );
	}

	void RecordEmptyRound( double roundAt, bool held )
	{
		emptyRoundAt = null;
		if ( !PrimaryAutomatic || BurstLength > 1 || HasIdleMagazineBarrel || !IsHeld || Clip1 != 1 ) return;
		emptyRoundAt = roundAt;
		// A held trigger attempts one dry shot before the barrel enters recovery.
		barrelIdleAt = roundAt + (held ? PrimaryDelay : 0) + 1 / 30.0 + RecoveryDelay;
	}

	protected virtual void FireRound( Vector3 forward )
	{
		if ( IsHeld && !TakePrimaryAmmo() ) return;
		LaunchBolt( forward );
		TimeSinceShoot = 0;
		ShootEffects();
	}

	public override bool CanReload() => !(dryFiring && emptyRoundAt.HasValue) && base.CanReload();

	protected override void OnControl()
	{
		// Resolve earlier weapon ticks before input. A melee on this tick takes
		// priority over a continuation round due on the same tick.
		double tick = System.Math.Floor( Time.NowDouble * 30 );
		if ( remainingShots > 0 ) AdvanceBurst( tick - 1 );
		bool held = Input.Down( TriggerButton );
		if ( RecoveryBufferTicks > 0 )
		{
			double remaining = recoveryEndsAt - Time.NowDouble;
			if ( !CanUseHeldWeapon || MeleeActive || IsReloading || !HasPrimaryAmmo() ) triggerPressedDuringRecovery = false;
			else if ( remaining > 0 && remaining <= RecoveryBufferTicks / 30.0 && Input.Pressed( TriggerButton ) )
				triggerPressedDuringRecovery = true;
		}
		if ( !held ) triggerPressedDuringReload = false;
		else if ( !PrimaryAutomatic && IsReloading && Input.Pressed( TriggerButton ) ) triggerPressedDuringReload = true;
		if ( emptyRoundAt is double shot && !held )
		{
			double elapsedTicks = System.Math.Max( 1, System.Math.Floor( (Time.NowDouble - shot) * 30 ) );
			barrelIdleAt = System.Math.Min( barrelIdleAt, shot + elapsedTicks / 30 + RecoveryDelay );
		}
		base.OnControl();
		if ( remainingShots > 0 ) AdvanceBurst( tick );
	}

	void AdvanceBurst( double throughTick )
	{
		while ( remainingShots > 0 && System.Math.Round( burstRoundAt * 30 ) <= throughTick && NextPrimaryFire <= 0 )
		{
			if ( !FirePrimary( burstAttacker ) ) break;
		}
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
		if ( IsHeld && IsInHand && AutoReload && Clip1 == 0 ) BeginReload( automatic: true );
	}

	void BeginReload( bool automatic )
	{
		if ( !CanReload() ) return;
		CancelBurst();
		if ( Networking.IsHost )
		{
			emptyRoundAt = null;
			transferAt = null;
			transferred = false;
			var animation = FirstPersonAnimations();
			var actions = IsDualWielding ? animation?.DualActions ?? animation?.Actions : animation?.Actions;
			if ( actions?.TryGetValue( Clip1 == 0 ? "reload_empty" : "reload_full", out var clip ) == true )
			{
				// Auto-reload is requested after the current tick's magazine update.
				int startTick = automatic ? 1 : 0;
				double now = Time.NowDouble;
				double startedAt = System.Math.Floor( now * 30 ) / 30;
				// The barrel becomes ready on the tick after the magazine finishes its animation.
				int finishTicks = clip.Frames + startTick + 1;
				ReloadTime = (float)System.Math.Max( 0, startedAt + finishTicks / 30.0 - now );
				if ( clip.Frames > 0 )
				{
					var cue = clip.Cues.FirstOrDefault( cue => cue.Kind == "primary_keyframe" );
					int ticks = cue?.Frame is int key && key >= 0 && key <= clip.Frames ? key : clip.Frames;
					transferAt = startedAt + (System.Math.Max( 0, ticks - 1 ) + startTick) / 30.0;
					// Holstering transfers the rounds once fewer than half the transfer ticks remain.
					holsterTransferAt = startedAt + (ticks / 2 + 1 + startTick) / 30.0;
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
		transferAt = null;
		return base.OnHolstering( next );
	}

	protected override void OnHolstered()
	{
		// A forced drop cancels the holder's animation, but the loose magazine keeps ticking.
		if ( !IsReloading ) transferAt = null;
		base.OnHolstered();
	}

	protected override void OnEquipped()
	{
		transferAt = null;
		base.OnEquipped();
	}

	void UpdateLooseMagazine()
	{
		if ( transferAt is not double due || Time.NowDouble < due ) return;
		Clip1 += System.Math.Min( LooseReserve, ReloadFillTarget - Clip1 );
		transferAt = null;
	}

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		if ( !IsHeld ) UpdateLooseMagazine();
		return base.OnAdding( inventory );
	}

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		base.OnAdded( inventory );
		if ( transferAt.HasValue && Time.NowDouble >= holsterTransferAt )
			TransferMagazine();
		transferAt = null;
	}

	protected override void UpdateWeaponState()
	{
		base.UpdateWeaponState();
		if ( Networking.IsHost && !IsHeld ) UpdateLooseMagazine();
		if ( IsReloading && transferAt is double due && Time.NowDouble >= due )
			TransferMagazine();
		if ( remainingShots > 0 && Holder is not PlayerController )
			AdvanceBurst( System.Math.Floor( Time.NowDouble * 30 ) );
		if ( IsReloading ) emptyRoundAt = null;
		if ( IsProxy && !(Networking.IsHost && emptyRoundAt.HasValue) ) return;
		if ( !IsInHand || !IsHeld || !AutoReload || Clip1 != 0 || !CanReload() ) return;
		if ( HasIdleMagazineBarrel || (emptyRoundAt.HasValue || BurstLength > 1 ? Time.NowDouble >= barrelIdleAt : NextPrimaryFire <= 0) )
			ReloadEmptyBarrel();
	}

	protected override void OnDisabled()
	{
		triggerPressedDuringRecovery = false;
		recoveryEndsAt = 0;
		triggerPressedDuringReload = false;
		CancelBurst();
		emptyRoundAt = null;
		base.OnDisabled();
	}

	public override bool CanSecondaryAttack() =>
		(remainingShots == 0 || System.Math.Floor( Time.NowDouble * 30 ) > burstStartTick) && base.CanSecondaryAttack();

	protected override void PlayMeleeView( string action )
	{
		CancelBurst();
		base.PlayMeleeView( action );
	}

	void CancelBurst()
	{
		remainingShots = 0;
		burstAttacker = null;
	}
}
