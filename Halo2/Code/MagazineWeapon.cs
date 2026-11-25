namespace Halo2;

public sealed class MagazineWeapon : HandheldWeapon
{
	[Property] public int BurstLength { get; set; }
	[Property] public float RecoveryDelay { get; set; }
	[Property] public bool HasIdleMagazineBarrel { get; set; }
	int remainingShots;
	double burstRoundAt;
	GameObject burstAttacker;
	double? transferAt;
	bool transferred;

	protected override bool HoldingAutomaticTrigger => BurstLength <= 1 && base.HoldingAutomaticTrigger;

	public override void PrimaryAttack()
	{
		RecordRound();
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
		else
		{
			// Keep each round on the burst's timeline when a frame lands late.
			burstRoundAt += PrimaryDelay;
			SetNextPrimaryFire( (float)System.Math.Max( 0, burstRoundAt - Time.NowDouble ) );
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
				if ( clip.Frames > 0 )
				{
					var cue = clip.Cues.FirstOrDefault( cue => cue.Kind == "primary_keyframe" );
					int frame = cue?.Frame is int key && key >= 0 && key < clip.Frames ? key : clip.Frames - 1;
					transferAt = Time.NowDouble + frame / 30.0;
				}
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
