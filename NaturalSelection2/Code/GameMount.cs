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

	private static ResourceType GetFileType( string extension ) => extension.ToLowerInvariant() switch
	{
		".cinematic" => ResourceType.Binary,
		".model" => ResourceType.Model,
		".animation_graph" => ResourceType.AnimationGraph,
		".dds" => ResourceType.Texture,
		".material" => ResourceType.Material,
		".fsb" => ResourceType.Sound,
		".level" => ResourceType.Scene,
		_ => ResourceType.None
	};

	protected override Task Mount( MountContext context )
	{
		if ( FileSystem is null )
			return Task.CompletedTask;

		Skulk.RegisterSounds( context );
		Fade.RegisterSounds( context );
		Gorge.RegisterSounds( context );
		Lerk.RegisterSounds( context );
		Babbler.RegisterSounds( context );
		context.Add( ResourceType.GameResource, $"sounds/{Hydra.AttackSound}.sound", new SparkSoundLoader( Hydra.AttackSound ) );
		Mac.RegisterSounds( context );
		context.Add( ResourceType.GameResource, $"sounds/{Marine.RifleSound}.sound", new SparkSoundLoader( Marine.RifleSound ) );
		context.Add( ResourceType.GameResource, $"sounds/{Drifter.ConstructSound}.sound", new SparkSoundLoader( Drifter.ConstructSound ) );
		Onos.RegisterSounds( context );
		var npcModels = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
		{
			NpcPrefab.Register( context, "Skulk", Skulk.Configure ),
			NpcPrefab.Register( context, "Fade", Fade.Configure ),
			NpcPrefab.Register( context, "Gorge", Gorge.Configure ),
			NpcPrefab.Register( context, "Lerk", Lerk.Configure ),
			NpcPrefab.Register( context, "Drifter", Drifter.Configure, "ns2_drifter" ),
			NpcPrefab.Register( context, "Harvester", Harvester.Configure, "ns2_harvester" ),
			NpcPrefab.Register( context, "Marine", Marine.Configure, "ns2_marine", "marine", "male" ),
			NpcPrefab.Register( context, "MAC", Mac.Configure, "ns2_mac", "marine" ),
			NpcPrefab.Register( context, "Hydra", Hydra.Configure, "ns2_hydra" ),
			NpcPrefab.Register( context, "Babbler", Babbler.Configure, "ns2_babbler" ),
			NpcPrefab.Register( context, "Onos", ( root, renderer ) => Onos.Configure( root, renderer, this ) )
		};
		foreach ( var path in FileSystem.FindFile( "", "*", true ) )
		{
			var resourceType = GetFileType( Path.GetExtension( path ) );

			if ( resourceType == ResourceType.Model )
			{
				context.Add( resourceType, path, new ModelLoader( path ) { IsNpc = npcModels.Contains( path ) } );
			}
			else if ( resourceType == ResourceType.AnimationGraph )
			{
				var model = Path.ChangeExtension( path, ".model" );
				if ( GetByPath( $"mount://{Ident}/{path}.ag" ) is null && FileSystem.FileExists( model ) )
					context.Add( resourceType, path, new AnimationGraphLoader( model, path ) );
			}
			else if ( resourceType == ResourceType.Binary )
			{
				context.Add( resourceType, path, new CinematicLoader( path ) );
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
