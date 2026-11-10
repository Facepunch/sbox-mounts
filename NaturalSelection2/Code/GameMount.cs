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

	internal BaseFileSystem FileSystem { get; private set; }

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( appId ) )
			return;

		FileSystem = context.GetFileSystem( appId );
		IsInstalled = FileSystem is not null;
	}

	internal byte[] ReadAllBytes( string path )
	{
		using var stream = FileSystem.OpenRead( path );
		var bytes = new byte[checked((int)stream.Length)];
		stream.ReadExactly( bytes );
		return bytes;
	}

	private static readonly Dictionary<string, ResourceType> FileTypes = new()
	{
		{ ".model", ResourceType.Model },
		{ ".dds", ResourceType.Texture },
		{ ".material", ResourceType.Material },
		{ ".fsb", ResourceType.Sound },
	};

	protected override Task Mount( MountContext context )
	{
		if ( FileSystem is null )
			return Task.CompletedTask;

		foreach ( var path in FileSystem.FindFile( "", "*", true ) )
		{
			var ext = Path.GetExtension( path )?.ToLower();
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
			else if ( resourceType == ResourceType.Sound )
			{
				SoundBankLoader.AddSoundsFromBank( context, FileSystem, path );
			}
		}

		IsMounted = true;

		return Task.CompletedTask;
	}
}
