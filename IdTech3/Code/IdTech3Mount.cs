using IdTech3.Formats;

namespace IdTech3;

/// <summary>
/// Shared implementation for idTech 3 games. Each game directory (baseq3, missionpack, Main, uo...)
/// is exposed as a layered virtual file system that follows the idTech 3 search order:
/// mod directory before base directory, loose files before archives, later archives before earlier ones.
/// </summary>
public abstract partial class IdTech3Mount : BaseGameMount
{
	/// <summary>Steam app this game is installed with.</summary>
	protected abstract long AppId { get; }

	/// <summary>Additional app that must be installed for this mount (e.g. United Offensive needs Call of Duty).</summary>
	protected virtual long? RequiredAppId => null;

	/// <summary>The base game directory, e.g. "baseq3" or "Main".</summary>
	protected abstract string BaseDirectory { get; }

	/// <summary>When set, only this directory is mounted, layered on top of <see cref="BaseDirectory"/>.</summary>
	protected virtual string ModDirectory => null;

	/// <summary>Mount every other directory that contains archives as a mod of the base game.</summary>
	protected virtual bool MountOtherDirectories => false;

	/// <summary>Directories never treated as mods.</summary>
	protected virtual string[] IgnoredDirectories => [];

	protected virtual string[] ArchiveExtensions => [".pk3"];

	public abstract GameProfile Profile { get; }

	public override long? SteamAppId => AppId;

	BaseFileSystem _fileSystem;
	string _baseDirName;
	readonly Dictionary<string, GameData> _games = new( StringComparer.OrdinalIgnoreCase );
	readonly List<IDisposable> _archives = [];

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( AppId ) )
			return;
		if ( RequiredAppId is long required && !context.IsAppInstalled( required ) )
			return;

		_fileSystem = context.GetFileSystem( AppId );
		if ( _fileSystem is null )
			return;

		_baseDirName = FindDirectory( BaseDirectory );
		if ( _baseDirName is null )
			return;

		if ( ModDirectory is not null && FindDirectory( ModDirectory ) is null )
			return;

		IsInstalled = true;
	}

	/// <summary>Resolve a (possibly nested, e.g. "GameData/base") directory name case-insensitively.</summary>
	string FindDirectory( string name )
	{
		var current = "";
		foreach ( var segment in name.Split( '/', StringSplitOptions.RemoveEmptyEntries ) )
		{
			string match = null;
			foreach ( var dir in _fileSystem.FindDirectory( current ) )
			{
				var clean = dir.Replace( '\\', '/' ).Trim( '/' );
				var last = clean.Contains( '/' ) ? clean[(clean.LastIndexOf( '/' ) + 1)..] : clean;
				if ( last.Equals( segment, StringComparison.OrdinalIgnoreCase ) ) { match = last; break; }
			}
			if ( match is null ) return null;
			current = current.Length == 0 ? match : $"{current}/{match}";
		}
		return current.Length > 0 ? current : null;
	}

	/// <summary>Get the data for a mounted game directory.</summary>
	public GameData GetGame( string dir ) => _games.TryGetValue( dir, out var g ) ? g : null;

	/// <summary>The game directory that provides a file source itself.</summary>
	public GameData OwnerOf( IFileSource source ) => _games.Values.FirstOrDefault( g => g.OwnSources.Contains( source ) );

	public IEnumerable<GameData> Games => _games.Values;

	List<IFileSource> OwnSources( string dir )
	{
		var list = new List<IFileSource>();
		var loose = new LooseSource( _fileSystem, dir, ArchiveExtensions );
		list.Add( loose );

		var archives = new List<string>();
		foreach ( var file in _fileSystem.FindFile( dir, "*", false ) )
		{
			var ext = Path.GetExtension( file )?.ToLowerInvariant();
			if ( ext is not null && ArchiveExtensions.Contains( ext ) )
				archives.Add( file.Replace( '\\', '/' ) );
		}

		// later archives override earlier ones (pak8 > pak0, zz_mod > pak0)
		archives.Sort( StringComparer.OrdinalIgnoreCase );
		archives.Reverse();

		foreach ( var archive in archives )
		{
			var full = $"{dir}/{archive}";
			var zip = new ZipSource( archive, () => _fileSystem.OpenRead( full ) );
			_archives.Add( zip );
			list.Add( zip );
		}
		return list;
	}

	protected override Task Mount( MountContext context )
	{
		var baseSources = OwnSources( _baseDirName );
		var mounted = new List<(string Dir, List<IFileSource> Own, List<IFileSource> All)>();

		if ( ModDirectory is not null )
		{
			var modName = FindDirectory( ModDirectory );
			var own = OwnSources( modName );
			mounted.Add( (modName, own, own.Concat( baseSources ).ToList()) );
		}
		else
		{
			mounted.Add( (_baseDirName, baseSources, baseSources) );

			if ( MountOtherDirectories )
			{
				foreach ( var dir in _fileSystem.FindDirectory( "" ) )
				{
					var clean = dir.Replace( '\\', '/' ).Trim( '/' );
					if ( clean.Equals( _baseDirName, StringComparison.OrdinalIgnoreCase ) ) continue;
					if ( IgnoredDirectories.Any( x => x.Equals( clean, StringComparison.OrdinalIgnoreCase ) ) ) continue;

					bool hasArchives = _fileSystem.FindFile( clean, "*", false )
						.Any( f => ArchiveExtensions.Contains( Path.GetExtension( f )?.ToLowerInvariant() ?? "" ) );
					if ( !hasArchives ) continue;

					var own = OwnSources( clean );
					mounted.Add( (clean, own, own.Concat( baseSources ).ToList()) );
				}
			}
		}

		foreach ( var (dir, own, all) in mounted )
		{
			var key = (dir.Contains( '/' ) ? dir[(dir.LastIndexOf( '/' ) + 1)..] : dir).ToLowerInvariant();
			var game = new GameData( this, key, new LayeredFileSystem( key, all ), Profile, own );
			_games[key] = game;
			RegisterAll( context, game, own );
		}

		IsMounted = true;
		return Task.CompletedTask;
	}
}

/// <summary>
/// Loose files inside a game directory on disk.
/// </summary>
sealed class LooseSource : IFileSource
{
	readonly BaseFileSystem _fs;
	readonly string _dir;
	readonly Dictionary<string, string> _files = new( StringComparer.OrdinalIgnoreCase );

	public string Name => _dir;
	public IEnumerable<string> Files => _files.Keys;

	public LooseSource( BaseFileSystem fs, string dir, string[] archiveExtensions )
	{
		_fs = fs;
		_dir = dir;

		foreach ( var file in fs.FindFile( dir, "*", true ) )
		{
			var rel = file.Replace( '\\', '/' );
			var ext = Path.GetExtension( rel )?.ToLowerInvariant() ?? "";
			if ( archiveExtensions.Contains( ext ) ) continue;
			if ( ext is ".exe" or ".dll" or ".so" or ".dylib" or ".cfg" or ".log" or ".dat" ) continue;
			_files.TryAdd( PathUtil.Normalize( rel ), rel );
		}
	}

	public bool Contains( string path ) => _files.ContainsKey( path );

	public byte[] Read( string path )
	{
		if ( !_files.TryGetValue( path, out var rel ) ) return null;
		try
		{
			return _fs.ReadAllBytes( $"{_dir}/{rel}" ).ToArray();
		}
		catch ( Exception )
		{
			return null;
		}
	}
}

/// <summary>
/// Per-game constants that differ between idTech 3 titles.
/// </summary>
public sealed class GameProfile
{
	public bool Has3dSkyboxes = false;

	/// <summary>
	/// RTCW / ET doors: opened with use rather than by walking into them (unless flagged TOUCH), rotating doors
	/// swing away from whoever opens them, and the "type" key picks the door%i_* sound set.
	/// </summary>
	public bool ActivatedDoors = false;

	public bool CallOfDuty;

	/// <summary>
	/// Bits to shift lightmap and vertex lighting by (r_mapOverBrightBits minus the hardware overbright),
	/// with the colour-preserving clamp from R_ColorShiftLightingBytes.
	/// </summary>
	public int LightShift = 2;

	/// <summary>Image extensions tried, in order, when a shader references an image without one.</summary>
	public string[] ImageExtensions = [".tga", ".jpg", ".png"];

	/// <summary>How the BSP's surface flags encode a surface's material (footsteps, impacts).</summary>
	public SurfaceFlagStyle SurfaceFlags;
}
