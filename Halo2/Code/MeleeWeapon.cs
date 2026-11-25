namespace Halo2;

public class MeleeWeapon : HandheldWeapon
{
	public MeleeWeapon()
	{
		UsesAmmo = UsesClips = false;
		PrimaryAutomatic = false;
	}

	public override bool CanPrimaryAttack() => NextPrimaryFire <= 0 && CanSecondaryAttack();
	public override void PrimaryAttack() => BeginMelee( true );

	public override bool CanReload() => false;
}
