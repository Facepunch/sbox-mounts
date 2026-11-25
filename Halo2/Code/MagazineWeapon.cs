namespace Halo2;

public sealed class MagazineWeapon : HandheldWeapon
{
	[Property] public int BurstLength { get; set; }
	[Property] public float RecoveryDelay { get; set; }
	int remainingShots;
	GameObject burstAttacker;

	public override void PrimaryAttack()
	{
		if ( BurstLength > 1 && remainingShots == 0 )
		{
			remainingShots = BurstLength;
			burstAttacker = Attacker;
		}
		if ( Instantaneous ) base.PrimaryAttack();
		else if ( Networking.IsHost ) FireRound( AimRay.Forward );
		else
		{
			OnShootEffects( default );
			TimeSinceShoot = 0;
			RequestRound( AimRay.Forward );
		}
		if ( remainingShots == 0 ) return;
		if ( --remainingShots == 0 || (IsHeld && !HasPrimaryAmmo()) )
		{
			remainingShots = 0;
			burstAttacker = null;
			SetNextPrimaryFire( RecoveryDelay );
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestRound( Vector3 forward )
	{
		if ( !IsActive || !IsHeld || !CanPrimaryAttack() || !forward.LengthSquared.AlmostEqual( 1, 0.001f ) ) return;
		SetNextPrimaryFire( PrimaryDelay );
		FireRound( forward );
	}

	void FireRound( Vector3 forward )
	{
		if ( IsHeld && !TakePrimaryAmmo() ) return;
		LaunchBolt( forward );
		TimeSinceShoot = 0;
		ShootEffects();
	}

	public override bool CanReload() => remainingShots == 0 && base.CanReload();

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( remainingShots > 0 && NextPrimaryFire <= 0 )
			FirePrimary( burstAttacker );
	}

	protected override void OnDisabled()
	{
		remainingShots = 0;
		burstAttacker = null;
		base.OnDisabled();
	}

	public override bool CanSecondaryAttack() => remainingShots == 0 && base.CanSecondaryAttack();
}
