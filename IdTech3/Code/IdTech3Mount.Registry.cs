using IdTech3.Formats;

namespace IdTech3;

/// <summary>
/// Registers every asset a game directory provides with the mount system.
/// </summary>
public abstract partial class IdTech3Mount
{
	static readonly string[] SkySuffixes = ["_bk", "_ft", "_lf", "_rt", "_up", "_dn"];

	public static void RegisterAll( MountContext context, GameData game, List<IFileSource> ownSources )
	{
		var own = new HashSet<IFileSource>( ownSources );
		var registered = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var images = new List<string>();

		foreach ( var path in game.Fs.Files.OrderBy( x => x, StringComparer.Ordinal ) )
		{
			// only register what this directory provides (or overrides)
			if ( !own.Contains( game.Fs.SourceOf( path ) ) )
				continue;

			var ext = PathUtil.Extension( path );
			var full = $"{game.Dir}/{path}";

			switch ( ext )
			{
				case ".bsp":
					if ( path.StartsWith( "maps/", StringComparison.Ordinal ) )
					{
						Add( context, registered, ResourceType.Scene, full, new MapLoader( game, path ) );
						// RTCW / ET sky portal, drawn by the map's Skybox3D (empty for maps without one)
						if ( game.Profile.Has3dSkyboxes )
						{
							Add( context, registered, ResourceType.Scene, full + "_skybox", new MapLoader( game, path, skybox: true ) { Flags = ResourceFlags.DeveloperOnly } );
						}
					}
					break;

				case ".md3":
				case ".mdc":
				case ".mds":
					Add( context, registered, ResourceType.Model, full, new ModelLoader( game, path ) );
					RegisterPlayer( context, registered, game, path );
					break;

				case ".wav":
				case ".mp3":
				case ".ogg":
					Add( context, registered, ResourceType.Sound, full, new SoundLoader( game, path, false ) );
					// looping variant for looped speakers
					Add( context, registered, ResourceType.Sound, full + ".loop", new SoundLoader( game, path, true ) { Flags = ResourceFlags.DeveloperOnly } );
					break;

				case "":
					if ( game.Profile.CallOfDuty && path.StartsWith( "xmodel/", StringComparison.Ordinal ) )
						Add( context, registered, ResourceType.Model, full, new XModelLoader( game, path["xmodel/".Length..] ) );
					break;

				default:
					if ( PathUtil.IsImage( path ) )
					{
						Add( context, registered, ResourceType.Texture, full, new TextureLoader( game, path ) );
						images.Add( path );
						RegisterSky( context, registered, game, path );
					}
					break;
			}
		}

		// shader scripts defined by this directory become materials, and their animMaps atlases
		var shaderNames = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var def in game.Shaders.All )
		{
			if ( def.SourceFile is null || !own.Contains( game.Fs.SourceOf( def.SourceFile ) ) ) continue;
			shaderNames.Add( def.Name );
			foreach ( var stage in def.Stages )
			{
				if ( stage.Maps.Count < 2 ) continue;
				Add( context, registered, ResourceType.Texture, $"{game.Dir}/{GameData.AnimAtlasName( stage.Maps )}",
					new AnimAtlasLoader( game, stage.Maps.ToArray() ) { Flags = ResourceFlags.DeveloperOnly } );
			}

			// GameData.GetModelMaterial asks for exactly these: drawable script shaders, whatever images they use
			if ( MaterialBuilder.IsInvisible( def ) || def.IsSky || def.IsFog ) continue;
			Add( context, registered, ResourceType.Material, $"{game.Dir}/{def.Name}", new MaterialLoader( game, def.Name ) );
		}

		foreach ( var image in images )
		{
			if ( !image.StartsWith( "textures/", StringComparison.Ordinal ) && !image.StartsWith( "models/", StringComparison.Ordinal ) && !image.StartsWith( "skins/", StringComparison.Ordinal ) )
				continue;
			var shader = PathUtil.StripExtension( image );
			if ( shaderNames.Contains( shader ) || game.Shaders.Find( shader ) is not null ) continue;
			Add( context, registered, ResourceType.Material, $"{game.Dir}/{shader}", new MaterialLoader( game, shader ) );
		}
	}

	static void RegisterSky( MountContext context, HashSet<string> registered, GameData game, string path )
	{
		var stem = PathUtil.StripExtension( path );
		if ( !stem.EndsWith( "_bk", StringComparison.Ordinal ) ) return;
		var box = stem[..^3];
		// need at least most of the faces
		int found = SkySuffixes.Count( s => game.FindImage( box + s ) is not null );
		if ( found < 5 ) return;
		Add( context, registered, ResourceType.Texture, $"{game.Dir}/{box}_cube", new SkyCubeLoader( game, box ) );
		Add( context, registered, ResourceType.Material, $"{game.Dir}/{box}", new SkyboxLoader( game, box ) );
	}

	static void RegisterPlayer( MountContext context, HashSet<string> registered, GameData game, string path )
	{
		// models/players/NAME/lower.md3 + upper.md3 + head.md3 -> one assembled player model
		if ( !path.StartsWith( "models/players/", StringComparison.Ordinal ) ) return;
		var file = PathUtil.StripExtension( PathUtil.FileName( path ) );
		if ( file != "lower" ) return;
		var dir = PathUtil.Directory( path );
		var ext = PathUtil.Extension( path );
		if ( !game.Fs.Exists( $"{dir}/upper{ext}" ) || !game.Fs.Exists( $"{dir}/head{ext}" ) ) return;
		Add( context, registered, ResourceType.Model, $"{game.Dir}/{dir}/player", new PlayerModelLoader( game, dir, ext ) );
	}

	static void Add( MountContext context, HashSet<string> registered, ResourceType type, string path, ResourceLoader loader )
	{
		if ( !registered.Add( $"{type}:{path}" ) ) return;
		context.Add( type, path, loader );
	}
}
