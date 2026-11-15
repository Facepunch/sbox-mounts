namespace Source1;

internal sealed class SourceAmmoLoader( string name, int maxReserve ) : ResourceLoader<SourceGameMount>
{
	protected override object Load() => new BaseAmmoResource { Title = name, MaxReserve = maxReserve };
}
