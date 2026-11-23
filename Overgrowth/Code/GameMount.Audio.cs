using System;

namespace Overgrowth;

public partial class GameMount
{
	Dictionary<string, int> phonemes;
	internal Dictionary<string, int> Phonemes => phonemes ??= VoiceTrack.ReadGroups( FileSystem.ReadAllText( Resolve( "Data/Sounds/voice/phonemes.txt" ) ) );
	readonly Dictionary<string, XmlAsset> materials = new();
	readonly Dictionary<string, (int Variants, float Volume, float Distance)> soundGroups = new();

	XmlAsset ReadMaterial( string path )
	{
		if ( !materials.TryGetValue( path, out var xml ) )
			materials[path] = xml = FileSystem.FileExists( path ) ? XmlAsset.Read( FileSystem, path, "material" ) : null;
		return xml;
	}

	internal float MaterialHardness( string surface ) =>
		ReadMaterial( $"Data/Materials/{surface ?? "default"}.xml" )?.Element( "physics" )?.Number( "hardness" ) ?? 1f;

	internal bool AllowsPenetration( string surface ) => ReadMaterial( $"Data/Materials/{surface ?? "default"}.xml" )?.Element( "physics" )?.Number( "sharp_penetration" ) > 0f;

	internal void PlayMaterialEvent( CharacterLoader character, GameObject actor, string surface, string step, Vector3 position, float gain )
	{
		PlaySurfaceEvent( actor, surface, step, position, gain, character.SoundMod, true );
		if ( character.Clothing is { Length: > 0 } clothing )
			PlayEvent( ReadMaterial( clothing ), null, actor, step, position, gain, true );
	}

	internal void PlaySurfaceEvent( GameObject actor, string surface, string step, Vector3 position, float gain, string mod, bool useMaterialDistance = false )
	{
		var material = ReadMaterial( $"Data/Materials/{surface ?? "default"}.xml" ) ?? ReadMaterial( "Data/Materials/default.xml" );
		PlayEvent( material, mod, actor, step, position, gain, useMaterialDistance );
	}

	void PlayEvent( XmlAsset material, string mod, GameObject actor, string step, Vector3 position, float gain, bool useMaterialDistance )
	{
		var events = material?.Elements( "events" );
		var entry = events?.FirstOrDefault( e => e.Attribute( "mod" ) == mod )?.Element( step )
			?? events?.FirstOrDefault( e => e.Attribute( "mod" ) is null )?.Element( step );
		if ( entry?.Attribute( "soundgroup" ) is not { } group ) return;
		var handle = PlayGroup( group, position, gain, entry.Attribute( "attached" ) == "true" ? actor : null );
		if ( !handle.IsValid() ) return;
		if ( useMaterialDistance )
		{
			float distance = entry.Number( "max_distance" ) ?? ReadMaterial( "Data/Materials/base.xml" )?.Element( "events" )?.Element( step )?.Number( "max_distance" ) ?? 0f;
			if ( distance > 0 ) handle.Distance = distance * gain * 40f;
		}
	}

	internal (SoundFile File, float Volume, float Distance) PickSound( string path )
	{
		if ( !soundGroups.TryGetValue( path, out var group ) )
		{
			var xml = XmlAsset.Read( FileSystem, path, "soundgroup" );
			soundGroups[path] = group = ((int)(xml.Number( "variants" ) ?? 1), xml.Number( "volume" ) ?? 1, (xml.Number( "max_distance" ) ?? 30f) * 40f);
		}
		int variant = Random.Shared.Next( 1, group.Variants + 1 );
		var file = SoundFile.Load( MountedPath( $"{path[..^4]}_{variant}.wav" ) + ".vsnd" );
		return (file, group.Volume, group.Distance);
	}

	internal SoundHandle PlayGroup( string path, Vector3 position, float gain = 1f, GameObject parent = null )
	{
		var (file, volume, distance) = PickSound( path );
		if ( file is null ) return null;
		float level = volume * gain;
		var handle = Sound.PlayFile( file, Math.Min( 1f, level ) );
		if ( handle.IsValid() )
		{
			// OpenAL caps the attenuated gain at one, with a one-metre reference distance.
			handle.MinDistance = 40f * Math.Max( 1f, level );
			handle.Distance = distance;
			handle.Position = position;
			if ( parent.IsValid() )
			{
				handle.Parent = parent;
				handle.LocalTransform = parent.WorldTransform.ToLocal( new Transform( position ) );
				handle.FollowParent = true;
			}
		}
		return handle;
	}
}
