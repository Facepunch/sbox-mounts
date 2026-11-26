namespace Source1;

internal sealed class SourceAmmoLoader( string name, int maxReserve ) : ResourceLoader<SourceGameMount>
{
	protected override object Load()
	{
		BaseAmmoResource ammo = Host is Hl2.Mount or GarrysMod.Mount ? new GarrysMod.Ammo() : new BaseAmmoResource();
		ammo.Title = name;
		ammo.MaxReserve = maxReserve;
		return ammo;
	}
}
