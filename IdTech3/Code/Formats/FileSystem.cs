using System.IO.Compression;

namespace IdTech3.Formats;

/// <summary>
/// A source of files: a pk3/iwd archive or a loose directory.
/// Paths are normalized (lowercase, forward slashes, relative to the game directory).
/// </summary>
public interface IFileSource
{
	string Name { get; }
	IEnumerable<string> Files { get; }
	bool Contains( string path );
	byte[] Read( string path );
}

/// <summary>
/// A pk3 (Quake 3, RTCW, CoD) or iwd (CoD2) archive. These are plain zip files.
/// </summary>
public sealed class ZipSource : IFileSource, IDisposable
{
	readonly Func<Stream> _open;
	readonly object _lock = new();
	readonly Dictionary<string, string> _entries = new( StringComparer.OrdinalIgnoreCase );
	Stream _stream;
	ZipArchive _zip;

	public string Name { get; }
	public IEnumerable<string> Files => _entries.Keys;

	public ZipSource( string name, Func<Stream> open )
	{
		Name = name;
		_open = open;

		lock ( _lock )
		{
			if ( !EnsureOpen() ) return;

			foreach ( var entry in _zip.Entries )
			{
				// directories end in '/'
				if ( entry.FullName.Length == 0 || entry.FullName.EndsWith( "/" ) || entry.FullName.EndsWith( "\\" ) )
					continue;

				var path = PathUtil.Normalize( entry.FullName );
				_entries.TryAdd( path, entry.FullName );
			}
		}
	}

	bool EnsureOpen()
	{
		if ( _zip is not null ) return true;
		try
		{
			_stream = _open();
			if ( _stream is null ) return false;
			_zip = new ZipArchive( _stream, ZipArchiveMode.Read, false );
			return true;
		}
		catch ( Exception )
		{
			_stream?.Dispose();
			_stream = null;
			_zip = null;
			return false;
		}
	}

	public bool Contains( string path ) => _entries.ContainsKey( path );

	public byte[] Read( string path )
	{
		if ( !_entries.TryGetValue( path, out var fullName ) )
			return null;

		lock ( _lock )
		{
			if ( !EnsureOpen() ) return null;
			var entry = _zip.GetEntry( fullName );
			if ( entry is null ) return null;

			try
			{
				using var s = entry.Open();
				var result = new byte[entry.Length];
				int read = 0;
				while ( read < result.Length )
				{
					int n = s.Read( result, read, result.Length - read );
					if ( n <= 0 ) break;
					read += n;
				}
				return read == result.Length ? result : result[..read];
			}
			catch ( Exception )
			{
				return null;
			}
		}
	}

	public void Dispose()
	{
		lock ( _lock )
		{
			_zip?.Dispose();
			_stream?.Dispose();
			_zip = null;
			_stream = null;
		}
	}
}

/// <summary>
/// An in-memory source (used by tests and for generated files).
/// </summary>
public sealed class MemorySource : IFileSource
{
	readonly Dictionary<string, byte[]> _files = new( StringComparer.OrdinalIgnoreCase );
	public string Name { get; }
	public MemorySource( string name ) { Name = name; }
	public void Add( string path, byte[] data ) => _files[PathUtil.Normalize( path )] = data;
	public IEnumerable<string> Files => _files.Keys;
	public bool Contains( string path ) => _files.ContainsKey( path );
	public byte[] Read( string path ) => _files.TryGetValue( path, out var d ) ? d : null;
}

/// <summary>
/// A stack of file sources searched in priority order, mirroring the idTech3 search path:
/// for each game directory (mod first, then base), loose files win over archives and
/// archives are searched in reverse alphabetical order (pak8 overrides pak0).
/// </summary>
public sealed class LayeredFileSystem
{
	readonly List<IFileSource> _sources;
	readonly Dictionary<string, IFileSource> _index = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The game directory this view represents (e.g. "baseq3", "main", "uo").</summary>
	public string GameDir { get; }

	/// <param name="gameDir">Name of the top game directory.</param>
	/// <param name="sources">Highest priority first.</param>
	public LayeredFileSystem( string gameDir, IEnumerable<IFileSource> sources )
	{
		GameDir = gameDir;
		_sources = sources.ToList();

		foreach ( var src in _sources )
			foreach ( var f in src.Files )
				_index.TryAdd( f, src );
	}

	public IReadOnlyList<IFileSource> Sources => _sources;

	/// <summary>Every unique file visible in this view.</summary>
	public IEnumerable<string> Files => _index.Keys;

	public bool Exists( string path ) => !string.IsNullOrEmpty( path ) && _index.ContainsKey( PathUtil.Normalize( path ) );

	public IFileSource SourceOf( string path ) => _index.TryGetValue( PathUtil.Normalize( path ), out var s ) ? s : null;

	public byte[] Read( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return null;
		var p = PathUtil.Normalize( path );
		return _index.TryGetValue( p, out var src ) ? src.Read( p ) : null;
	}

	public string ReadText( string path )
	{
		var d = Read( path );
		return d is null ? null : System.Text.Encoding.Latin1.GetString( d );
	}

	/// <summary>Find the first existing file among <paramref name="basePath"/> + each extension.</summary>
	public string FindWithExtension( string basePath, params string[] extensions )
	{
		var p = PathUtil.Normalize( basePath );
		foreach ( var ext in extensions )
		{
			var candidate = p + ext;
			if ( _index.ContainsKey( candidate ) ) return candidate;
		}
		return null;
	}

	/// <summary>All files under a directory with the given extension (e.g. "scripts", ".shader").</summary>
	public IEnumerable<string> FindFiles( string directory, string extension )
	{
		var dir = PathUtil.Normalize( directory );
		if ( dir.Length > 0 && !dir.EndsWith( "/" ) ) dir += "/";
		return _index.Keys.Where( f => f.StartsWith( dir, StringComparison.Ordinal ) && f.EndsWith( extension, StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( f => f, StringComparer.Ordinal );
	}
}
