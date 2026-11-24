namespace Diesel;

/// <summary>Default static unit geometry for asset-browser previews and model references.</summary>
sealed class DieselModelLoader( string unit ) : ResourceLoader<PaydayMount>
{
	protected override object Load()
	{
		var result = UnitModel.Build( Host.Database, Host.BrowserMaterials, unit, $"mount://payday/{unit}.unit.vmdl" );
		return result?.Model ?? throw new InvalidDataException( $"PAYDAY unit {unit} has no supported runtime model." );
	}
}
