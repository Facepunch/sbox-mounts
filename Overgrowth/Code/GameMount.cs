using Sandbox.Mounting;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Overgrowth;

public partial class GameMount : BaseGameMount
{
	public override string Ident => "overgrowth";
	public override string Title => "Overgrowth";
	public override long? SteamAppId => appId;

	/// <summary>
	/// Read-only filesystem rooted at the game's install directory.
	/// </summary>
	internal BaseFileSystem FileSystem { get; private set; }
	const long appId = 25000;

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( appId ) ) return;
		FileSystem = context.GetFileSystem( appId );
		IsInstalled = FileSystem is not null && FileSystem.DirectoryExists( "Data" );
	}

	protected override Task Mount( MountContext context )
	{
		if ( !FileSystem.DirectoryExists( "Data" ) )
			return Task.CompletedTask;

		foreach ( var relative in FileSystem.FindFile( "Data", "*", true ) )
		{
			var path = $"Data/{relative}";
			var ext = Path.GetExtension( path ).ToLowerInvariant();

			switch ( ext )
			{
				case ".dds":
					context.Add( ResourceType.Texture, path, new DdsTextureLoader( path ) );
					break;
				case ".tga":
				case ".png":
				case ".jpg":
				case ".jpeg":
				case ".bmp":
				case ".gif":
					context.Add( ResourceType.Texture, path, new ImageTextureLoader( path ) );
					break;
				case ".wav":
				case ".ogg":
				case ".mp3":
					context.Add( ResourceType.Sound, path, new SoundLoader( path ) );
					break;
				case ".obj":
					context.Add( ResourceType.Model, path, new ObjectLoader( path ) );
					break;
				case ".xml":
					MountXml( context, path );
					break;
			}
		}

		foreach ( var actor in RabbitActor.All )
		{
			var definition = XmlAsset.Read( FileSystem, actor.Path, "Actor" );
			var character = Resolve( definition.Element( "Character" ).Value );
			context.Add( ResourceType.Model, actor.ModelName, new CharacterLoader( character, true ) { Flags = ResourceFlags.DeveloperOnly } );
			var prefab = new RabbitGuardPrefab( actor );
			prefab.Tags.Add( "npc" );
			context.Add( ResourceType.PrefabFile, actor.PrefabName, prefab );
		}

		IsMounted = true;
		return Task.CompletedTask;
	}

	void MountXml( MountContext context, string path )
	{
		using var stream = new StreamReader( FileSystem.OpenRead(path) );
		string line;
		while ( (line = stream.ReadLine()) is not null )
		{
			line = line.TrimStart();
			if (line.Length == 0 || line.StartsWith("<?")) continue;
			if (line.StartsWith("<Type>"))
			{
				if (path.StartsWith("Data/Levels/",StringComparison.OrdinalIgnoreCase) && line.Contains(">saved</Type>"))
				{
					context.Add(ResourceType.Scene,path,new LevelLoader(path));
					context.Add(ResourceType.Model,path,new TerrainLoader(path){Flags=ResourceFlags.DeveloperOnly});
					return;
				}
				continue;
			}
			var match = Regex.Match(line, @"^<([\w:-]+)");
			switch (match.Groups[1].Value)
			{
			case "character": context.Add(ResourceType.Model,path,new CharacterLoader(path)); break;
			case "Object":
				context.Add(ResourceType.Model,path,new ObjectLoader(path));
				context.Add(ResourceType.Material,path,new MaterialLoader(path,"Object")); break;
			case "DecalObject": context.Add(ResourceType.Material,path,new MaterialLoader(path,"DecalObject")); break;
			case "item": context.Add(ResourceType.Model,path,new ObjectLoader(path,true)); break;
			}
			return;
		}
	}

	/// <summary>
	/// Turns a dependency path from a game asset into a normalized path relative to the install root.
	/// </summary>
	internal string Resolve( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			throw new InvalidDataException( "Overgrowth asset has an empty dependency path." );

		return Normalize( path.Trim() )
			?? throw new InvalidDataException( $"Overgrowth dependency is outside the game directory: {path}" );
	}

	/// <summary>
	/// Collapses "." and ".." segments, returning null if the path is rooted or escapes the install root.
	/// </summary>
	static string Normalize( string path )
	{
		path = path.Replace( '\\', '/' );
		if ( Path.IsPathRooted( path ) || path.Contains( ':' ) )
			return null;

		var parts = new List<string>();
		foreach ( var part in path.Split( '/' ) )
		{
			if ( part.Length == 0 || part == "." )
				continue;

			if ( part == ".." )
			{
				if ( parts.Count == 0 )
					return null;

				parts.RemoveAt( parts.Count - 1 );
				continue;
			}

			parts.Add( part );
		}

		return parts.Count == 0 ? null : string.Join( '/', parts );
	}

	internal string MountedPath( string path ) => $"mount://{Ident}/{path.Trim().Replace( '\\', '/' )}";

	internal Texture LoadTexture( string path )
	{
		var filePath = Resolve( path );
		if ( FileSystem.FileExists( filePath + "_converted.dds" ) )
			path = path.Trim() + "_converted.dds";
		else if ( !FileSystem.FileExists( filePath ) )
			throw new FileNotFoundException( "Overgrowth texture was not found.", filePath );
		var texture = Texture.Load( MountedPath( path ) + ".vtex" );
		if ( texture is null || texture.IsError )
			throw new InvalidDataException( $"Unable to decode Overgrowth texture {path}." );
		return texture;
	}

	readonly Dictionary<string, Vector4> averageColors = new( StringComparer.OrdinalIgnoreCase );

	internal Vector4 AverageColor( string path, Texture texture )
	{
		if ( averageColors.TryGetValue( path, out var average ) )
			return average;
		var pixels = texture.GetPixels();
		if ( pixels.Length == 0 )
			throw new InvalidDataException( $"Unable to read detail texture {path}." );
		foreach ( var pixel in pixels )
			average += new Vector4( pixel.r, pixel.g, pixel.b, pixel.a );
		average /= pixels.Length * 255f;
		average = new Vector4( MathF.Pow( average.x, 2.2f ), MathF.Pow( average.y, 2.2f ), MathF.Pow( average.z, 2.2f ), average.w );
		averageColors.Add( path, average );
		return average;
	}

	protected override void Shutdown()
	{
		averageColors.Clear();
		audioMaterials.Clear();
		soundGroups.Clear();
	}
}
