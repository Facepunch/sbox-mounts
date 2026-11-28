using System;

namespace Source1;

public abstract partial class SourceGameMount
{
	internal bool IsFemaleActor( string model )
	{
		if ( string.IsNullOrEmpty( model ) ) return false;
		var name = Path.GetFileNameWithoutExtension( model );
		if ( name.EndsWith( ".mdl", StringComparison.OrdinalIgnoreCase ) ) name = Path.GetFileNameWithoutExtension( name );
		return index.FemaleActors.Contains( name );
	}

	private void MountSoundEvents( MountContext context, FileIndex files )
	{
		const string manifest = "scripts/game_sounds_manifest.txt";
		if ( !files.Files.ContainsKey( manifest ) ) return;
		var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		try
		{
			const string actors = "scripts/global_actors.txt";
			if ( files.Files.TryGetValue( actors, out var actorFile ) )
				foreach ( var block in ScriptManifest.Parse( actorFile.ReadText(), actors, message => Log.Trace( message ) ) )
					foreach ( var actor in block.Children )
						if ( string.Equals( actor.Value, "female", StringComparison.OrdinalIgnoreCase ) )
							files.FemaleActors.Add( actor.Name );

			foreach ( var path in ScriptManifest.Read( files.Files[manifest].ReadText(), manifest, "game_sounds_manifest", "precache_file", "declare_file", "preload_file" ) )
			{
				if ( !files.Files.ContainsKey( path ) ) continue;
				try
				{
					foreach ( var root in ScriptManifest.Parse( files.Files[path].ReadText(), path, message => Log.Trace( message ) ).Where( x => x.IsBlock ) )
					{
						if ( names.Contains( root.Name ) ) continue;
						try
						{
							var definition = SourceSoundDefinition.Parse( root, path );
							var name = $"sounds/events/{Uri.EscapeDataString( root.Name.ToLowerInvariant() )}";
							if ( definition.Waves.Any( w => w.Contains( "$gender" ) ) )
							{
								SourceSoundDefinition For( string gender ) => definition with { Waves = definition.Waves.Select( w => w.Replace( "$gender", gender ) ).ToArray() };
								if ( ResolveSurfaceSound( For( "female" ) ) is { } female )
									context.Add( ResourceType.GameResource, $"{name}.female.sound", new SurfaceSoundLoader( female ) );
								definition = For( "male" );
							}
							definition = ResolveSurfaceSound( definition );
							if ( definition is null ) continue;
							context.Add( ResourceType.GameResource, $"{name}.sound", new SurfaceSoundLoader( definition ) );
							if ( definition.Encoding == WaveEncoding.Doppler )
							{
								context.Add( ResourceType.GameResource, $"{name}.incoming.sound", new SurfaceSoundLoader( definition, 0 ) );
								context.Add( ResourceType.GameResource, $"{name}.outgoing.sound", new SurfaceSoundLoader( definition, 1 ) );
							}
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
	}
}
