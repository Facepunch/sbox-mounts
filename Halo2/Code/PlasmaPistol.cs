namespace Halo2;

public sealed class PlasmaPistol : BatteryWeapon
{
	[Property] public int LatchTicks { get; set; }
	[Property] public int ChargeTicks { get; set; }
	[Property] public EnergyDischarge ChargedShot { get; set; }

	[Sync( SyncFlags.FromHost )] bool HoldingTrigger { get; set; }
	[Sync( SyncFlags.FromHost )] double PressedAt { get; set; }
	bool showingCharge;

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
		var emitter = WeaponModel?.MuzzleGameObject;
		if ( !emitter.IsValid() ) emitter = GameObject;
		emitter.PlaySound( ChargedShot.Sound );
		ViewModel?.GetComponent<HandheldWeaponModel>()?.Play( "fire_2" );
		PlayHolderOverlay( "fire_2" );
		PostWeaponEvent( WeaponEvents.Attack );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		bool charged = FullyCharged;
		if ( charged && !showingCharge ) ViewModel?.GetComponent<HandheldWeaponModel>()?.Play( "overcharged" );
		showingCharge = charged;
	}

	protected override void OnHolstered()
	{
		if ( Networking.IsHost ) HoldingTrigger = false;
		showingCharge = false;
		base.OnHolstered();
	}
}
