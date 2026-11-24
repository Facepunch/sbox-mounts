using System.Text;

namespace Diesel;

public sealed class PaydayMount : BaseGameMount
{
	public override string Ident => "payday";
	public override string Title => "PAYDAY: The Heist";
	public override long? SteamAppId => 24240;
	BaseFileSystem fileSystem;
	internal BundleDatabase Database { get; private set; }
	MapMaterials browserMaterials;
	internal MapMaterials BrowserMaterials => browserMaterials ??= new MapMaterials( Database, "browser" );

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( 24240 ) ) return;
		fileSystem = context.GetFileSystem( 24240 );
		IsInstalled = fileSystem?.FileExists( "assets/all.blb" ) == true;
	}

	protected override Task Mount( MountContext context )
	{
		if ( !IsInstalled ) return Task.CompletedTask;
		Database = new BundleDatabase( path => fileSystem.OpenRead( path ),
			fileSystem.FindFile( "assets", "*_h.bundle", false ).Select( name => $"assets/{name}" ) );
		ulong worldType = IdString.Hash( "world" );
		var worlds = Database.Entries.Values.Where( e => e.Type == worldType ).ToDictionary( e => e.Path );
		ulong textureType = IdString.Hash( "texture" );
		ulong unitType = IdString.Hash( "unit" );
		ulong materialType = IdString.Hash( "material_config" );
		// The installed game supplies its own path dictionary. No external hashlist or extracted assets required.
		var lookup = Database.Find( "idstring_lookup", "idstring_lookup" );
		if ( lookup is null ) throw new InvalidDataException( "PAYDAY idstring lookup is missing." );
		int maps = 0, textures = 0, models = 0, materials = 0;
		foreach ( var name in Encoding.Latin1.GetString( lookup ).Split( '\0' ).Distinct() )
		{
			ulong hash = IdString.Hash( name );
			if ( Database.Entries.ContainsKey( (materialType, hash) ) )
			{
				try
				{
					// A config contains multiple independently named material slots. Hashes
					// preserve identity even when names contain path separators or punctuation.
					foreach ( var (slot, definition) in MaterialConfig.Read( Database.Find( "material_config", name ) ) )
					{
						if ( definition.Hidden ) continue;
						context.Add( ResourceType.Material, $"{name}.material_config/{slot:x16}",
							new DieselMaterialLoader( name, slot, definition.Name ) );
						materials++;
					}
				}
				catch ( Exception e ) { Log.Warning( $"PAYDAY material index {name}: {e.Message}" ); }
			}
			if ( Database.Entries.ContainsKey( (textureType, hash) ) )
			{
				context.Add( ResourceType.Texture, $"{name}.texture", new DieselTextureLoader( name ) );
				textures++;
			}
			if ( Database.Entries.ContainsKey( (unitType, hash) ) )
			{
				try
				{
					if ( UnitModel.DescribeForBrowser( Database, name ) is { } info )
					{
						context.Add( ResourceType.Model, $"{name}.unit", new DieselModelLoader( name, info ) );
						models++;
					}
				}
				catch ( Exception e ) { Log.Warning( $"PAYDAY model index {name}: {e.Message}" ); }
			}
			if ( name.StartsWith( "levels/", StringComparison.Ordinal ) && worlds.ContainsKey( hash ) )
			{
				context.Add( ResourceType.Scene, $"{name}.world", new MapLoader( name ) );
				maps++;
			}
		}
		if ( maps == 0 ) throw new InvalidDataException( "No PAYDAY worlds were found in the installed bundles." );
		Log.Info( $"PAYDAY: indexed {Database.Entries.Count} resources, {maps} worlds, {textures} textures, {models} models and {materials} materials." );
		IsMounted = true;
		return Task.CompletedTask;
	}

	protected override void Shutdown()
	{
		browserMaterials = null;
		Database = null;
	}
}
