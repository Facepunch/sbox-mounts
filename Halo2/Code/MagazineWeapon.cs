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
		base.PrimaryAttack();
		if ( remainingShots == 0 ) return;
		if ( --remainingShots == 0 || (IsHeld && !HasPrimaryAmmo()) )
		{
			remainingShots = 0;
			burstAttacker = null;
			SetNextPrimaryFire( RecoveryDelay );
		}
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
