using System;

namespace Overgrowth;

public partial class GameMount
{
	readonly Dictionary<(string Surface, string Event), string> stepGroups = new();
	readonly Dictionary<string, (int Variants, float Volume)> soundGroups = new();

	internal void PlayStep( string surface, string step, Vector3 position )
	{
		surface ??= "default";
		if ( !stepGroups.TryGetValue( (surface, step), out var group ) )
		{
			var path = $"Data/Materials/{surface}.xml";
			if ( !FileSystem.FileExists( path ) ) path = "Data/Materials/default.xml";
			group = XmlAsset.Read( FileSystem, path, "material" ).Element( "events" )?.Element( step )?.Attribute( "soundgroup" );
			stepGroups[(surface, step)] = group;
		}
		if ( group is not null ) PlayGroup( group, position );
	}

	internal (SoundFile File, float Volume) PickSound( string path )
	{
		if ( !soundGroups.TryGetValue( path, out var group ) )
		{
			var xml = XmlAsset.Read( FileSystem, path, "soundgroup" );
			soundGroups[path] = group = ((int)(xml.Number( "variants" ) ?? 1), xml.Number( "volume" ) ?? 1);
		}
		int variant = Random.Shared.Next( 1, group.Variants + 1 );
		var file = SoundFile.Load( MountedPath( $"{path[..^4]}_{variant}.wav" ) + ".vsnd" );
		return (file, group.Volume);
	}

	internal void PlayGroup( string path, Vector3 position )
	{
		var (file, volume) = PickSound( path );
		if ( file is null ) return;
		var handle = Sound.PlayFile( file, volume );
		if ( handle.IsValid() ) handle.Position = position;
	}
}
