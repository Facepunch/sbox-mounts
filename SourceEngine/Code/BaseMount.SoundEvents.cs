using System;

namespace Source1;

public abstract partial class BaseMount
{
	private void MountSoundEvents( MountContext context, FileIndex files )
	{
		const string manifest = "scripts/game_sounds_manifest.txt";
		if ( !files.Files.ContainsKey( manifest ) ) return;
		var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		try
		{
			foreach ( var path in ScriptManifest.Read( Read( manifest ), manifest, "game_sounds_manifest", "precache_file", "declare_file", "preload_file" ) )
			{
				if ( !files.Files.ContainsKey( path ) ) continue;
				try
				{
					foreach ( var root in ScriptManifest.Parse( Read( path ), path, message => Log.Trace( message ) ).Where( x => x.IsBlock ) )
					{
						if ( names.Contains( root.Name ) ) continue;
						try
						{
							var definition = ResolveSurfaceSound( SourceSoundDefinition.Parse( root, path ) );
							if ( definition is null ) continue;
							context.Add( ResourceType.GameResource, $"sounds/events/{Uri.EscapeDataString( root.Name.ToLowerInvariant() )}.sound", new SurfaceSoundLoader( definition ) );
							names.Add( root.Name );
						}
						catch ( Exception e ) when ( e is InvalidDataException or NotSupportedException )
						{
							Log.Trace( $"{Title}: sound event '{root.Name}' was not imported: {e.Message}" );
						}
					}
				}
				catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
				{
					Log.Trace( $"{Title}: sound events from '{path}' were not imported: {e.Message}" );
				}
			}
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
		{
			Log.Trace( $"{Title}: sound-event manifest was not imported: {e.Message}" );
		}

		string Read( string path )
		{
			using var stream = files.Files[path].Open();
			using var reader = new StreamReader( stream );
			return reader.ReadToEnd();
		}
	}
}
