using System;

namespace Overgrowth;

public partial class GameMount
{
	readonly Dictionary<string, XmlAsset> materials = new();
	readonly Dictionary<string, (int Variants, float Volume)> soundGroups = new();

	XmlAsset ReadMaterial( string path )
	{
		if ( !materials.TryGetValue( path, out var xml ) )
			materials[path] = xml = FileSystem.FileExists( path ) ? XmlAsset.Read( FileSystem, path, "material" ) : null;
		return xml;
	}

	internal float MaterialHardness( string surface ) =>
		ReadMaterial( $"Data/Materials/{surface ?? "default"}.xml" )?.Element( "physics" )?.Number( "hardness" ) ?? 1f;

	internal void PlayMaterialEvent( CharacterLoader character, GameObject actor, string surface, string step, Vector3 position, float gain )
	{
		var floor = ReadMaterial( $"Data/Materials/{surface ?? "default"}.xml" ) ?? ReadMaterial( "Data/Materials/default.xml" );
		Play( floor, character.SoundMod );
		if ( character.Clothing is { Length: > 0 } clothing ) Play( ReadMaterial( clothing ), null );

		void Play( XmlAsset material, string mod )
		{
			var events = material?.Elements( "events" );
			var entry = events?.FirstOrDefault( e => e.Attribute( "mod" ) == mod )?.Element( step )
				?? events?.FirstOrDefault( e => e.Attribute( "mod" ) is null )?.Element( step );
			if ( entry?.Attribute( "soundgroup" ) is not { } group ) return;
			var handle = PlayGroup( group, position, gain );
			if ( !handle.IsValid() ) return;
			float distance = entry.Number( "max_distance" ) ?? ReadMaterial( "Data/Materials/base.xml" )?.Element( "events" )?.Element( step )?.Number( "max_distance" ) ?? 0f;
			if ( distance > 0 ) handle.Distance = distance * gain * 40f;
			if ( entry.Attribute( "attached" ) != "true" ) return;
			handle.Parent = actor;
			handle.LocalTransform = actor.WorldTransform.ToLocal( new Transform( position ) );
			handle.FollowParent = true;
		}
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

	internal SoundHandle PlayGroup( string path, Vector3 position, float gain = 1f )
	{
		var (file, volume) = PickSound( path );
		if ( file is null ) return null;
		var handle = Sound.PlayFile( file, volume * gain );
		if ( handle.IsValid() ) handle.Position = position;
		return handle;
	}
}
