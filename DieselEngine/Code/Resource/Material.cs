namespace Diesel;

/// <summary>Individual named slots in a Diesel material configuration, loaded on demand.</summary>
sealed class DieselMaterialLoader : ResourceLoader<PaydayMount>
{
	readonly string config;
	readonly ulong material;

	public DieselMaterialLoader( string config, ulong material, string name )
	{
		this.config = config;
		this.material = material;
		Tags.Add( name );
	}

	protected override object Load() => Host.BrowserMaterials.Resolve( config, material )
		?? throw new InvalidDataException( $"PAYDAY material {config}/{material:x16} has no visible surface." );
}
