using System;

namespace Magicka;

/// <summary>
/// Mounting implementation for Magicka (Steam app 42910).
/// </summary>
public sealed partial class MagickaMount : BaseGameMount
{
	public override string Ident => "magicka";
	public override string Title => "Magicka";

	private const long AppId = 42910;
	public override long? SteamAppId => AppId;

	internal BaseFileSystem FileSystem { get; private set; }

	protected override void Initialize( InitializeContext context )
	{
		IsInstalled = false;
		IsMounted = false;
		FileSystem = null;

		if ( !context.IsAppInstalled( AppId ) )
			return;

		FileSystem = context.GetFileSystem( AppId );
		IsInstalled = FileSystem is not null;
	}

	protected override Task Mount( MountContext context )
	{
		IsMounted = false;
		if ( FileSystem is null )
			return Task.CompletedTask;

		MountContent( context );
		IsMounted = true;
		return Task.CompletedTask;
	}

	protected override void Shutdown()
	{
		FileSystem = null;
		IsMounted = false;
	}

	/// <summary>A full URL for a mount path, such as <c>mount://magicka/models/x.vmdl</c>.</summary>
	internal string Url( string path, string extension ) => $"mount://{Ident}/{path}.{extension}";

	internal byte[] ReadAllBytes( string file ) => FileSystem.ReadAllBytes( file ).ToArray();

	internal string ReadTextFile( string file ) =>
		FileSystem is not null && FileSystem.FileExists( file ) ? FileSystem.ReadAllText( file ) : null;

	/// <summary>Mount path for a content file: no <c>Content/</c> prefix, no <c>.xnb</c> extension, lower case.</summary>
	internal static string ResourcePath( string file )
	{
		var path = file.Replace( '\\', '/' );
		if ( path.StartsWith( ContentPrefix, StringComparison.OrdinalIgnoreCase ) )
			path = path[ContentPrefix.Length..];

		if ( path.EndsWith( ".xnb", StringComparison.OrdinalIgnoreCase ) )
			path = path[..^4];

		return path.ToLowerInvariant();
	}

	/// <summary>Resolves an XNB external reference relative to <paramref name="sourceFile"/>, preserving case, without <c>.xnb</c>.</summary>
	internal static string ResolveExternalFile( string sourceFile, string relative )
	{
		if ( string.IsNullOrWhiteSpace( relative ) )
			return null;

		var directory = sourceFile.Replace( '\\', '/' );
		var slash = directory.LastIndexOf( '/' );
		directory = slash < 0 ? "" : directory[..slash];

		var parts = new List<string>( directory.Split( '/', StringSplitOptions.RemoveEmptyEntries ) );
		foreach ( var segment in relative.Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries ) )
		{
			if ( segment == ".." )
			{
				if ( parts.Count > 0 )
					parts.RemoveAt( parts.Count - 1 );
			}
			else if ( segment != "." )
			{
				parts.Add( segment );
			}
		}

		var full = string.Join( '/', parts );
		return full.StartsWith( ContentPrefix, StringComparison.OrdinalIgnoreCase ) ? full : null;
	}

	/// <summary>Resolves an XNB external reference to its normalized mount resource path, or null.</summary>
	internal static string ResolveExternalPath( string sourceFile, string relative )
	{
		var resolved = ResolveExternalFile( sourceFile, relative );
		return resolved is null ? null : ResourcePath( resolved );
	}
}
