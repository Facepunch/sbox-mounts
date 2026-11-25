namespace Halo2;

public sealed record ChargeSoundTrack( string Marker, SoundEvent Start, SoundEvent Loop, float Gain, float FadeOut );

public sealed class PlasmaPistol : BatteryWeapon
{
	[Property] public int LatchTicks { get; set; }
	[Property] public int ChargeTicks { get; set; }
	[Property] public EnergyDischarge ChargedShot { get; set; }
	[Property] public ChargeSoundTrack ChargeSound { get; set; }

	[Sync( SyncFlags.FromHost )] bool HoldingTrigger { get; set; }
	[Sync( SyncFlags.FromHost )] double PressedAt { get; set; }
	bool showingCharge;
	SoundHandle chargeVoice;
	bool chargeStarted;

	public bool FullyCharged => HoldingTrigger && Time.NowDouble >= PressedAt + (LatchTicks + ChargeTicks) / 30.0;
	internal override string RestAction => FullyCharged ? "overcharged" : base.RestAction;
	protected override bool WantsPrimaryAttack() => false;

	protected override void Think()
	{
		base.Think();
		if ( Input.Pressed( "attack1" ) ) SetTrigger( true );
		if ( Input.Released( "attack1" ) ) SetTrigger( false );
	}

	public void SetTrigger( bool pressed ) => UpdateTrigger( pressed, AimRay.Forward );

	[Rpc.Host( NetFlags.OwnerOnly )]
	void UpdateTrigger( bool pressed, Vector3 forward )
	{
		if ( !IsActive || pressed == HoldingTrigger ) return;
		if ( pressed )
		{
			if ( !CanPrimaryAttack() ) return;
			PressedAt = Time.NowDouble;
			HoldingTrigger = true;
			return;
		}

		bool charged = FullyCharged;
		HoldingTrigger = false;
		if ( !CanPrimaryAttack() || !forward.LengthSquared.AlmostEqual( 1, 0.001f ) ) return;
		SetNextPrimaryFire( PrimaryDelay );
		FireEnergy( forward, true, charged ? ChargedShot : null );
	}

	protected override void PresentDischarge() => PlayChargedShot();

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayChargedShot()
	{
		if ( Application.IsDedicatedServer ) return;
		SoundEmitter( ChargedShot.SoundMarker )?.PlaySound( ChargedShot.Sound );
		ViewModel?.GetComponent<HandheldWeaponModel>()?.Play( "fire_2" );
		PlayHolderOverlay( "fire_2" );
		PostWeaponEvent( WeaponEvents.Attack );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Application.IsDedicatedServer ) UpdateChargeSound();
		bool charged = FullyCharged;
		if ( charged && !showingCharge ) ViewModel?.GetComponent<HandheldWeaponModel>()?.Play( "overcharged" );
		showingCharge = charged;
	}

	void UpdateChargeSound()
	{
		if ( !HoldingTrigger || !IsActive || Time.NowDouble < PressedAt + LatchTicks / 30.0 )
		{
			if ( chargeStarted ) StopChargeSound();
			return;
		}
		if ( ChargeSound is not {} track || chargeVoice.IsValid() ) return;
		var emitter = SoundEmitter( track.Marker );
		if ( !emitter.IsValid() ) return;
		chargeVoice = emitter.PlaySound( chargeStarted ? track.Loop : track.Start ?? track.Loop );
		if ( chargeVoice.IsValid() ) chargeVoice.Volume *= track.Gain;
		chargeStarted = true;
	}

	void StopChargeSound()
	{
		chargeVoice?.Stop( ChargeSound?.FadeOut ?? 0 );
		chargeVoice = null;
		chargeStarted = false;
	}

	protected override void OnDisabled()
	{
		StopChargeSound();
		base.OnDisabled();
	}

	protected override void OnHolstered()
	{
		StopChargeSound();
		if ( Networking.IsHost ) HoldingTrigger = false;
		showingCharge = false;
		base.OnHolstered();
	}
}
