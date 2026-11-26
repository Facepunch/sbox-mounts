namespace Diesel;

/// <summary>Browser unit models, retaining authored character rigs independently of NPC definitions.</summary>
sealed class DieselModelLoader : ResourceLoader<PaydayMount>
{
	readonly string unit;
	readonly bool character;

	public DieselModelLoader( string unit, UnitModel.BrowseInfo info )
	{
		this.unit = unit;
		character = info.Kind == UnitKind.Character;
		Tags.Add( info.Kind.ToString().ToLowerInvariant() );
		Flags = ResourceFlags.NeverRagdoll;
		if ( info.Kind is UnitKind.Architecture or UnitKind.Character or UnitKind.Scripted ) Flags |= ResourceFlags.DeveloperOnly;
		if ( character ) Tags.Add( "skinned" );
		if ( info.Kind == UnitKind.Scripted ) Tags.Add( "animation-pending" );
		if ( !info.HasCollision ) Flags |= ResourceFlags.Effect;
	}

	protected override object Load()
	{
		if ( character ) return new PaydayCharacter( Host.Database, Host.BrowserMaterials, unit, Path ).Model;
		var result = UnitModel.BuildBrowser( Host.Database, Host.BrowserMaterials, unit, $"mount://payday/{unit}.unit.vmdl" );
		return result?.Model ?? throw new InvalidDataException( $"PAYDAY unit {unit} has no supported runtime model." );
	}
}
