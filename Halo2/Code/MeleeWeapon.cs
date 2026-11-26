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

public sealed class ObjectiveWeapon : MeleeWeapon
{
	public override void PrimaryAttack() => Inventory?.Drop( this );
}

public sealed class JuggernautPowerup : MeleeWeapon
{
	public override bool CanPrimaryAttack() => false;
	public override void PrimaryAttack() { }
}
