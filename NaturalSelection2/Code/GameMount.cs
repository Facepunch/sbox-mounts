using System;
using System.Collections.Concurrent;

namespace NS2;

/// <summary>
/// A mounting implementation for Natural Selection 2
/// </summary>
public class GameMount : BaseGameMount
{
	public override string Ident => "ns2";
	public override string Title => "Natural Selection 2";

	const long appId = 4920;

	public override long? SteamAppId => appId;

	internal readonly Dictionary<string, string[]> SoundBanks = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, SoundProject> soundProjects = new( StringComparer.OrdinalIgnoreCase );

	internal SoundProject.Event FindSoundEvent( string name )
	{
		int split = name?.IndexOf( ".fev/", StringComparison.OrdinalIgnoreCase ) ?? -1;
		if ( split < 0 ) return null;
		var project = name[..(split + 4)];
		if ( !soundProjects.TryGetValue( project, out var events ) )
		{
			if ( TryResolve( FileSystem, project, out var path ) )
				events = SoundProject.Read( ReadAllBytes( path ), path[..path.LastIndexOf( '/' )], SoundBanks );
			soundProjects[project] = events;
		}
		return events?.Events.GetValueOrDefault( name[(split + 5)..] );
	}

	internal Dictionary<string, Material> SkyMaterials { get; } = new( StringComparer.OrdinalIgnoreCase );
	internal ConcurrentDictionary<string, (string Key, string Path)[]> ShaderDefaults { get; } = new( StringComparer.OrdinalIgnoreCase );

	protected override void Shutdown()
	{
		ProjectedSpotLight.ClearCookies();
		SkyMaterials.Clear();
		SoundBanks.Clear();
		soundProjects.Clear();
		ShaderDefaults.Clear();
	}

	internal BaseFileSystem FileSystem { get; private set; }

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( appId ) )
			return;

		FileSystem = context.GetFileSystem( appId );
		IsInstalled = FileSystem is not null;
	}

	internal Surface MaterialSurface( string relative )
	{
		if ( relative?.StartsWith( "ns2/", StringComparison.OrdinalIgnoreCase ) == true ) relative = relative[4..];
		var name = TryResolve( FileSystem, relative, out var path ) && GetByPath( $"mount://ns2/{path}.vmat" ) is MaterialLoader loader
			? loader.SurfaceName : "metal";
		return GameResource.Load<Surface>( SurfaceLoader.PathFor( name ) );
	}

	internal byte[] ReadAllBytes( string path )
	{
		using var stream = FileSystem.OpenRead( path );
		var bytes = new byte[checked((int)stream.Length)];
		stream.ReadExactly( bytes );
		return bytes;
	}

	private static readonly string[] Branches = ["ns2", "core"];

	/// <summary>Spark resolves content against ns2 first, then core.</summary>
	internal static bool TryResolve( BaseFileSystem fs, string relative, out string path )
	{
		if ( !string.IsNullOrWhiteSpace( relative ) )
		{
			foreach ( var branch in Branches )
			{
				path = $"{branch}/{relative}".Replace( '\\', '/' );
				if ( fs.FileExists( path ) ) return true;
			}
		}

		path = null;
		return false;
	}

	private static readonly Dictionary<string, ResourceType> FileTypes = new( StringComparer.OrdinalIgnoreCase )
	{
		{ ".model", ResourceType.Model },
		{ ".dds", ResourceType.Texture },
		{ ".material", ResourceType.Material },
		{ ".fsb", ResourceType.Sound },
		{ ".level", ResourceType.Scene },
	};

	protected override Task Mount( MountContext context )
	{
		if ( FileSystem is null )
			return Task.CompletedTask;

		foreach ( var path in FileSystem.FindFile( "", "*", true ) )
		{
			var ext = Path.GetExtension( path );
			if ( string.IsNullOrWhiteSpace( ext ) )
				continue;

			if ( !FileTypes.TryGetValue( ext, out var resourceType ) )
				continue;

			if ( resourceType == ResourceType.Model )
			{
				context.Add( resourceType, path, new ModelLoader( path ) );
			}
			else if ( resourceType == ResourceType.Texture )
			{
				context.Add( resourceType, path, new DdsTextureLoader( path ) );
			}
			else if ( resourceType == ResourceType.Material )
			{
				context.Add( resourceType, path, new MaterialLoader( path ) );
			}
			else if ( resourceType == ResourceType.Scene )
			{
				context.Add( resourceType, path, new MapLoader( path ) );
			}
			else if ( resourceType == ResourceType.Sound )
			{
				SoundBanks[path] = SoundBankLoader.AddSoundsFromBank( context, FileSystem, path );
			}
		}

		SurfaceLoader.Register( context );
		IsMounted = true;

		return Task.CompletedTask;
	}
}
