namespace Halo2;

public sealed class EnergySword : HandheldWeapon
{
	public EnergySword()
	{
		UsesAmmo = UsesClips = false;
		PrimaryAutomatic = false;
	}

	public override bool CanPrimaryAttack() => NextPrimaryFire <= 0 && CanSecondaryAttack();
	public override void PrimaryAttack() => SecondaryAttack();
	public override bool CanReload() => false;
}
