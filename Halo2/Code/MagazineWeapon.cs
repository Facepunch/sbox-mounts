namespace Halo2;

public sealed class MagazineWeapon : HandheldWeapon
{
	[Property] public int BurstLength { get; set; }
	[Property] public float RecoveryDelay { get; set; }
	[Property] public bool HasIdleMagazineBarrel { get; set; }
	int remainingShots;
	GameObject burstAttacker;
	double? transferAt;
	bool transferred;

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

	public override void Reload()
	{
		if ( Networking.IsHost && CanReload() )
		{
			transferAt = null;
			transferred = false;
			if ( FirstPersonAnimations()?.Actions.TryGetValue( Clip1 == 0 ? "reload_empty" : "reload_full", out var clip ) == true )
			{
				ReloadTime = clip.Frames / 30f;
				if ( clip.Cues.FirstOrDefault( cue => cue.Kind == "primary_keyframe" )?.Frame is int frame && frame > 0 && frame < clip.Frames )
					transferAt = Time.NowDouble + frame / 30.0;
			}
		}
		base.Reload();
	}

	protected override int TakeReserveAmmo( BaseAmmoResource ammoType, int amount ) =>
		IsReloading && transferred && ammoType == PrimaryAmmoType ? 0 : base.TakeReserveAmmo( ammoType, amount );

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( IsReloading && transferAt is double due && Time.NowDouble >= due )
		{
			transferAt = null;
			Clip1 += TakeReserveAmmo( PrimaryAmmoType, ReloadFillTarget - Clip1 );
			transferred = true;
		}
		if ( remainingShots > 0 && NextPrimaryFire <= 0 )
			FirePrimary( burstAttacker );
		if ( !IsProxy && IsActive && IsHeld && AutoReload && Clip1 == 0 &&
			(HasIdleMagazineBarrel || NextPrimaryFire <= 0) && CanReload() )
			Reload();
	}

	protected override void OnDisabled()
	{
		remainingShots = 0;
		burstAttacker = null;
		base.OnDisabled();
	}

	public override bool CanSecondaryAttack() => remainingShots == 0 && base.CanSecondaryAttack();
}
