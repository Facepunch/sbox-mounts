namespace Diesel;

/// <summary>Default static unit geometry for asset-browser previews and model references.</summary>
sealed class DieselModelLoader : ResourceLoader<PaydayMount>
{
	readonly string unit;

	public DieselModelLoader( string unit, UnitModel.BrowseInfo info )
	{
		this.unit = unit;
		Tags.Add( info.Kind.ToString().ToLowerInvariant() );
		Flags = ResourceFlags.NeverRagdoll;
		if ( info.Kind is UnitKind.Architecture or UnitKind.Character or UnitKind.Scripted ) Flags |= ResourceFlags.DeveloperOnly;
		if ( info.Kind == UnitKind.Character ) Tags.Add( "rig-pending" );
		if ( info.Kind == UnitKind.Scripted ) Tags.Add( "animation-pending" );
		if ( !info.HasCollision ) Flags |= ResourceFlags.Effect;
	}

	protected override object Load()
	{
		var result = UnitModel.BuildBrowser( Host.Database, Host.BrowserMaterials, unit, $"mount://payday/{unit}.unit.vmdl" );
		return result?.Model ?? throw new InvalidDataException( $"PAYDAY unit {unit} has no supported runtime model." );
	}
}
