using System;
using System.Diagnostics;
using System.Text;

namespace Magicka;

public sealed partial class MagickaMount
{
	private const string ContentPrefix = "Content/";

	/// <summary>Enough compressed bytes to reach the reader table of any XNB in the game.</summary>
	private const int ClassifyBytes = 32 * 1024;

	/// <summary>Sorting the content means decompressing the head of every XNB; results are cached.</summary>
	private const string CachePath = "magicka/xnb_readers.txt";

	private readonly record struct CachedReader( long Size, string Reader );
	private readonly record struct IndexedTemplate( string File, CharacterTemplate Template );

	private void MountContent( MountContext context )
	{
		var started = Stopwatch.StartNew();

		var files = FileSystem.FindFile( "", "*", true )
			.Where( f => f.EndsWith( ".xnb", StringComparison.OrdinalIgnoreCase ) )
			.ToArray();

		var cache = ReadCache();
		var cacheChanged = false;

		var fileByResourcePath = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var modelFiles = new List<(string File, string Path)>();
		var textureFiles = new List<(string File, string Path)>();
		var levelFiles = new List<(string File, string Path)>();
		var templateFiles = new List<string>();
		var itemFiles = new List<string>();

		foreach ( var file in files )
		{
			var size = FileSize( file );
			if ( !cache.TryGetValue( file, out var entry ) || entry.Size != size )
			{
				entry = new CachedReader( size, PeekRootReader( file ) );
				cache[file] = entry;
				cacheChanged = true;
			}

			var path = ResourcePath( file );
			fileByResourcePath.TryAdd( path, file );

			switch ( entry.Reader )
			{
				case "ModelReader":
				case "SkinnedModelReader":
					modelFiles.Add( (file, path) );
					break;

				case "Texture2DReader":
				case "Texture3DReader":
					textureFiles.Add( (file, path) );
					break;

				case "CharacterTemplateReader":
					templateFiles.Add( file );
					break;

				case "ItemReader":
					itemFiles.Add( file );
					break;

				case "LevelModelReader":
					levelFiles.Add( (file, path) );
					break;
			}
		}

		if ( cacheChanged )
			WriteCache( cache );

		// Parse character templates so every character mesh receives its paired skeleton's clips
		// and animation-only skeleton files are not exposed as duplicate models.
		LoadItems( itemFiles );
		var templates = LoadCharacterTemplates( templateFiles );
		BuildAnimationPairing( templates, fileByResourcePath, out var modelToSkeleton, out var animationOnlyModels, out var modelSurfaces );

		var registeredModels = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (file, path) in modelFiles )
		{
			if ( animationOnlyModels.Contains( path ) )
				continue;

			modelToSkeleton.TryGetValue( path, out var skeletonFile );
			if ( !modelSurfaces.TryGetValue( path, out var surface )
				&& path.StartsWith( "models/characters/", StringComparison.OrdinalIgnoreCase ) )
				surface = "flesh";

			context.Add( ResourceType.Model, path, new ModelLoader( file, skeletonFile, surface ) );
			registeredModels.Add( path );
		}

		foreach ( var (file, path) in textureFiles )
			context.Add( ResourceType.Texture, path, new TextureLoader( file ) );

		foreach ( var (file, path) in levelFiles )
			context.Add( ResourceType.Scene, path, new LevelSceneLoader( file ) );

		var sounds = MountAudio( context );
		var npcs = MountEntities( context, templates, registeredModels );
		var weapons = MountWeapons( context, registeredModels );

		Log.Info( $"{Title}: {registeredModels.Count} models ({animationOnlyModels.Count} animation libraries merged), {textureFiles.Count} textures, {levelFiles.Count} levels, {sounds} sounds, {npcs} NPCs and {weapons} weapons in {started.ElapsedMilliseconds} ms" );
	}

	private List<IndexedTemplate> LoadCharacterTemplates( List<string> templateFiles )
	{
		var result = new List<IndexedTemplate>( templateFiles.Count );
		foreach ( var file in templateFiles )
		{
			try
			{
				var content = XnbContent.Read( ReadAllBytes( file ) );
				if ( content.Root is CharacterTemplate template )
					result.Add( new IndexedTemplate( file, template ) );
			}
			catch ( Exception e )
			{
				Log.Trace( $"{Title}: skipped character template {file}: {e.Message}" );
			}
		}

		return result;
	}

	private static void BuildAnimationPairing(
		List<IndexedTemplate> templates,
		Dictionary<string, string> fileByResourcePath,
		out Dictionary<string, string> modelToSkeleton,
		out HashSet<string> animationOnlyModels,
		out Dictionary<string, string> modelSurfaces )
	{
		modelToSkeleton = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		modelSurfaces = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var meshModels = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var skeletonModels = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var (file, template) in templates )
		{
			var skeletonPath = template.Skeleton is null ? null : ResolveExternalPath( file, template.Skeleton.Path );
			string skeletonFile = null;
			if ( skeletonPath is not null && fileByResourcePath.TryGetValue( skeletonPath, out skeletonFile ) )
				skeletonModels.Add( skeletonPath );

			var surface = SurfaceForBloodType( template.BloodType );

			foreach ( var variant in template.Models )
			{
				if ( variant.Model is null )
					continue;

				var meshPath = ResolveExternalPath( file, variant.Model.Path );
				if ( meshPath is null )
					continue;

				meshModels.Add( meshPath );
				modelSurfaces.TryAdd( meshPath, surface );
				if ( skeletonFile is not null && !string.Equals( meshPath, skeletonPath, StringComparison.OrdinalIgnoreCase ) )
					modelToSkeleton.TryAdd( meshPath, skeletonFile );
			}
		}

		animationOnlyModels = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var skeletonPath in skeletonModels )
		{
			if ( !meshModels.Contains( skeletonPath ) )
				animationOnlyModels.Add( skeletonPath );
		}
	}

	/// <summary>Magicka BloodType: 0 regular, 1 green, 2 black, 3 wood, 4 insect, 5 none.</summary>
	internal static string SurfaceForBloodType( int bloodType ) => bloodType switch
	{
		3 => "wood",
		5 => "default",
		_ => "flesh",
	};

	private long FileSize( string file )
	{
		try
		{
			return FileSystem.FileSize( file );
		}
		catch
		{
			return -1;
		}
	}

	private Dictionary<string, CachedReader> ReadCache()
	{
		var result = new Dictionary<string, CachedReader>( StringComparer.OrdinalIgnoreCase );
		try
		{
			var cacheFile = Sandbox.FileSystem.Data;
			if ( !cacheFile.FileExists( CachePath ) )
				return result;

			foreach ( var line in cacheFile.ReadAllText( CachePath ).Split( '\n' ) )
			{
				var parts = line.Split( '\t' );
				if ( parts.Length < 3 || !long.TryParse( parts[0], out var size ) )
					continue;

				result[parts[1]] = new CachedReader( size, parts[2] == "-" ? null : parts[2] );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"{Title}: could not read the XNB cache: {e.Message}" );
		}

		return result;
	}

	private void WriteCache( Dictionary<string, CachedReader> cache )
	{
		try
		{
			var data = Sandbox.FileSystem.Data;
			data.CreateDirectory( "magicka" );

			var text = new StringBuilder();
			foreach ( var (file, entry) in cache )
				text.Append( entry.Size ).Append( '\t' ).Append( file ).Append( '\t' ).Append( entry.Reader ?? "-" ).Append( '\n' );

			data.WriteAllText( CachePath, text.ToString() );
		}
		catch ( Exception e )
		{
			Log.Warning( $"{Title}: could not write the XNB cache: {e.Message}" );
		}
	}

	private string PeekRootReader( string file )
	{
		try
		{
			using var stream = FileSystem.OpenRead( file );

			var buffer = new byte[ClassifyBytes];
			var read = 0;
			while ( read < buffer.Length )
			{
				var got = stream.Read( buffer, read, buffer.Length - read );
				if ( got <= 0 )
					break;

				read += got;
			}

			var prefix = read == buffer.Length ? buffer : buffer.AsSpan( 0, read ).ToArray();
			return XnbContent.PeekRootReader( prefix );
		}
		catch ( Exception e )
		{
			Log.Warning( $"{Title}: could not read {file}: {e.Message}" );
			return null;
		}
	}
}
