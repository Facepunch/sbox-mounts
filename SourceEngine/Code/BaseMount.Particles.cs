using System;

namespace Source1;

public abstract partial class BaseMount
{
	private void MountParticles( MountContext context, FileIndex files )
	{
		var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var entry in files.Files.Where( x => x.Key.EndsWith( ".pcf", StringComparison.OrdinalIgnoreCase ) ).OrderBy( x => x.Key ) )
		{
			try
			{
				using var input = entry.Value.Open();
				using var data = new MemoryStream();
				input.CopyTo( data );
				var file = PcfFile.Read( data.ToArray() );
				foreach ( var system in file.Systems )
				{
					if ( !names.Add( system.Name ) ) continue;
					context.Add( ResourceType.PrefabFile, $"particles/{Uri.EscapeDataString( system.Name.ToLowerInvariant() )}.prefab",
						new SourceParticlePrefabLoader( entry.Key, system ) );
				}
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
			{
				Log.Warning( $"{entry.Key}: particles were not imported: {e.Message}" );
			}
		}
	}
}
