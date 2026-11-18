using System;

namespace Renderware;

/// <summary>A respawn point: GTA units, heading in degrees (0 = north, counter clockwise).</summary>
public readonly record struct RestartPoint( Vector3 Position, float Heading );

public abstract partial class GTAMount : BaseGameMount
{
	public abstract long AppId { get; }
	public abstract GameVersion Game { get; }
	public override long? SteamAppId => AppId;

	/// <summary>The game's main .dat, listing its IDE / IPL / COL files (data/default.dat is always loaded first).</summary>
	protected abstract string MainDat { get; }

	/// <summary>Image archives the game always opens, on top of any its .dat files list.</summary>
	protected virtual string[] Archives => ["models/gta3.img", "models/gta_int.img"];

	/// <summary>
	/// Where the game respawns you - its hospitals and police stations - used as the world's spawn points.
	/// Taken once from each game's main.scm (the add hospital / police restart commands) rather than parsing the
	/// script at runtime. GTA units; heading in degrees, 0 = north, counter clockwise; z of -100 means "on the
	/// ground", as in the game.
	/// </summary>
	public virtual RestartPoint[] RestartPoints => [];

	public BaseFileSystem FileSystem { get; private set; }

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( AppId ) )
			return;

		FileSystem = context.GetFileSystem( AppId );
		IsInstalled = FileSystem is not null;
	}

	protected override Task Mount( MountContext context )
	{
		// models and textures register as their files are found - the registry is the lookup (OpenModel, GetTexture)
		Load( context );

		foreach ( var file in FileSystem.FindFile( "/", "*.wav", true ) )
		{
			context.Add( ResourceType.Sound, file, new SoundLoaderWav( file ) );
		}

		foreach ( var file in FileSystem.FindFile( "/", ".mp3", true ) )
		{
			context.Add( ResourceType.Sound, file, new SoundLoaderMp3( file ) );
		}

		context.Add( ResourceType.Scene, "world", new WorldLoader() );

		IsMounted = true;
		return Task.CompletedTask;
	}

	protected override void Shutdown()
	{
		foreach ( var archive in archives )
			archive.Dispose();

		archives.Clear();
		ClearGameData();
	}

	/// <summary>
	/// Reads a file from the game folder, or null if it doesn't exist. Paths use / and are case insensitive.
	/// </summary>
	public Stream OpenFile( string path ) => FileSystem.FileExists( path ) ? FileSystem.OpenRead( path ) : null;
}
