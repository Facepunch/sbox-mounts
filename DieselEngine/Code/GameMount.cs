using System.Text;

namespace Diesel;

public sealed class PaydayMount : BaseGameMount
{
	public override string Ident => "payday";
	public override string Title => "PAYDAY: The Heist";
	public override long? SteamAppId => 24240;
	BaseFileSystem fileSystem;
	internal BundleDatabase Database { get; private set; }

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
		// The installed game supplies its own path dictionary. No external hashlist or extracted assets required.
		var lookup = Database.Find( "idstring_lookup", "idstring_lookup" );
		if ( lookup is null ) throw new InvalidDataException( "PAYDAY idstring lookup is missing." );
		int maps = 0, textures = 0;
		foreach ( var name in Encoding.Latin1.GetString( lookup ).Split( '\0' ).Distinct() )
		{
			ulong hash = IdString.Hash( name );
			if ( Database.Entries.ContainsKey( (textureType, hash) ) )
			{
				context.Add( ResourceType.Texture, $"{name}.texture", new DieselTextureLoader( name ) );
				textures++;
			}
			if ( name.StartsWith( "levels/", StringComparison.Ordinal ) && worlds.ContainsKey( hash ) )
			{
				context.Add( ResourceType.Scene, $"{name}.world", new MapLoader( name ) );
				maps++;
			}
		}
		if ( maps == 0 ) throw new InvalidDataException( "No PAYDAY worlds were found in the installed bundles." );
		Log.Info( $"PAYDAY: indexed {Database.Entries.Count} resources, {maps} worlds and {textures} textures." );
		IsMounted = true;
		return Task.CompletedTask;
	}

	protected override void Shutdown() => Database = null;
}
