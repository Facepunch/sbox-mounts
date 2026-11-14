namespace Source1;

internal sealed class SourceAmmoLoader( string name, int maxReserve ) : ResourceLoader<BaseMount>
{
	protected override object Load() => new BaseAmmoResource { Title = name, MaxReserve = maxReserve };
}
