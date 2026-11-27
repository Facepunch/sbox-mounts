using System;

namespace Source1.BlackMesa;

internal sealed partial class MapFormat
{
	private readonly Dictionary<string, string> thumbnails = new( StringComparer.OrdinalIgnoreCase );
	private Dictionary<string, string> chapters;

	internal override string MapThumbnail( string path ) => thumbnails.GetValueOrDefault( path );

	private void IndexThumbnail( string path, IReadOnlyDictionary<string, SourceGameMount.SourceFile> files )
	{
		var name = Path.GetFileNameWithoutExtension( path );
		var image = $"materials/vgui/multiplayer/map_backgrounds/{name}_0_widescreen.vtf";
		if ( !files.ContainsKey( image ) )
		{
			chapters ??= ReadChapters( files );
			var prefix = name;
			while ( !chapters.TryGetValue( prefix, out image ) )
			{
				if ( prefix.Length == 0 ) return;
				prefix = prefix[..^1];
			}
		}
		if ( files.ContainsKey( image ) ) thumbnails[path] = image;
	}

	private static Dictionary<string, string> ReadChapters( IReadOnlyDictionary<string, SourceGameMount.SourceFile> files )
	{
		var result = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var manifest = Read( "scripts/chapter_manifest.txt" );
		foreach ( var entry in manifest )
			result[entry.Key] = ChapterImage( entry.Value );

		// Some chapters list only their opening map (e.g. bm_c4a3a). Its siblings share the chapter image.
		foreach ( var family in manifest.Where( e => e.Key.Length > 1 && char.IsLetter( e.Key[^1] ) && char.IsDigit( e.Key[^2] ) )
			.GroupBy( e => e.Key[..^1] ) )
		{
			var chapter = family.First().Value;
			if ( family.All( e => e.Value == chapter ) ) result.TryAdd( family.Key, ChapterImage( chapter ) );
		}
		foreach ( var entry in Read( "scripts/chapterbackgrounds.txt" ) )
			result[entry.Value] = ChapterImage( entry.Key );
		return result;

		static string ChapterImage( string number ) => $"materials/vgui/chapters/chapter{number}.vtf";

		IEnumerable<KeyValuePair<string, string>> Read( string path )
		{
			if ( !files.TryGetValue( path, out var file ) ) return [];
			try
			{
				return VmtFile.ParseKeyValues( file.ReadText(), path ).FirstOrDefault()?.Values.AsEnumerable() ?? [];
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
			{
				Log.Warning( $"{path}: could not read map thumbnails: {e.Message}" );
				return [];
			}
		}
	}
}
