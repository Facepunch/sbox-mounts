namespace Halo2;

public sealed class PlasmaPistol : BatteryWeapon
{
	[Property] public int LatchTicks { get; set; }
	[Property] public int ChargeTicks { get; set; }
	[Property] public Dictionary<string, BulletContact> ChargedContacts { get; set; }
	protected override Dictionary<string, BulletContact> ContactsForProjectile( bool charged ) => charged ? ChargedContacts : BulletContacts;

	[Property] public EnergyDischarge ChargedShot { get; set; }
	[Property] public WeaponSoundTrack ChargeSound { get; set; }

	[Sync( SyncFlags.FromHost )] bool HoldingTrigger { get; set; }
	[Sync( SyncFlags.FromHost )] double PressedAt { get; set; }
	[Sync( SyncFlags.FromHost )] Vector3? PendingRelease { get; set; }
	bool showingCharge;
	readonly TrackVoice chargeVoice = new();

	double ChargedAt => PressedAt + (LatchTicks + ChargeTicks) / 30.0;
	public bool FullyCharged => (HoldingTrigger || PendingRelease.HasValue) && Time.NowDouble >= ChargedAt;
	internal override string RestAction => FullyCharged ? "overcharged" : base.RestAction;
	protected override bool WantsPrimaryAttack() => false;

	protected override void Think()
	{
		base.Think();
		if ( Input.Pressed( TriggerButton ) ) SetTrigger( true );
		if ( Input.Released( TriggerButton ) ) SetTrigger( false );
	}

	public void SetTrigger( bool pressed ) => UpdateTrigger( pressed, AimRay.Forward );

	[Rpc.Host( NetFlags.OwnerOnly )]
	void UpdateTrigger( bool pressed, Vector3 forward )
	{
		if ( !IsInHand || PendingRelease.HasValue || pressed == HoldingTrigger ) return;
		if ( pressed )
		{
			if ( !CanPrimaryAttack() ) return;
			PressedAt = Time.NowDouble;
			HoldingTrigger = true;
			return;
		}

		bool charged = FullyCharged;
		HoldingTrigger = false;
		if ( !forward.LengthSquared.AlmostEqual( 1, 0.001f ) ) return;
		// The trigger enters its charged state before it can discharge on the following tick.
		if ( charged && Time.NowDouble < ChargedAt + 1 / 30.0 )
		{
			PendingRelease = forward;
			return;
		}
		ReleaseShot( forward, charged );
	}

	void ReleaseShot( Vector3 forward, bool charged )
	{
		if ( !CanPrimaryAttack() ) return;
		SetNextPrimaryFire( PrimaryDelay );
		FireEnergy( forward, true, charged ? ChargedShot : null );
	}

	protected override void PresentDischarge() => PlayChargedShot();

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayChargedShot()
	{
		if ( Application.IsDedicatedServer ) return;
		PlayFiringSound( ChargedShot.Sound, ChargedShot.SoundMarker );
		ViewModel?.GetComponent<HandheldWeaponModel>()?.Play( "fire_2" );
		PlayHolderOverlay( "fire_2" );
		PostWeaponEvent( WeaponEvents.Attack );
	}

	protected override void UpdateWeaponState()
	{
		base.UpdateWeaponState();
		if ( Networking.IsHost && PendingRelease is Vector3 forward && Time.NowDouble >= ChargedAt + 1 / 30.0 )
		{
			PendingRelease = null;
			ReleaseShot( forward, true );
		}
		if ( !Application.IsDedicatedServer ) UpdateChargeSound();
		bool charged = FullyCharged;
		if ( charged && !showingCharge ) ViewModel?.GetComponent<HandheldWeaponModel>()?.Play( "overcharged" );
		showingCharge = charged;
	}

	void UpdateChargeSound()
	{
		UpdateTrack( chargeVoice, ChargeSound, HoldingTrigger && IsInHand && Time.NowDouble >= PressedAt + LatchTicks / 30.0 );
	}

	protected override void OnDisabled()
	{
		ResetTrigger();
		base.OnDisabled();
	}

	protected override void OnHolstered()
	{
		ResetTrigger();
		base.OnHolstered();
	}

	void ResetTrigger()
	{
		chargeVoice.Stop( ChargeSound );
		showingCharge = false;
		if ( !Networking.IsHost ) return;
		HoldingTrigger = false;
		PendingRelease = null;
	}
}
