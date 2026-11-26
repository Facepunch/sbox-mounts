using System;

namespace Overgrowth;

public partial class GameMount
{
	Dictionary<string, int> phonemes;
	internal Dictionary<string, int> Phonemes => phonemes ??= VoiceTrack.ReadGroups( FileSystem.ReadAllText( Resolve( "Data/Sounds/voice/phonemes.txt" ) ) );
	readonly Dictionary<string, XmlAsset> materials = new();
	readonly Dictionary<string, Queue<int>> voiceHistory = new();
	readonly Dictionary<string, (string[] Variants, float Volume, float Distance)> soundGroups = new();

	XmlAsset ReadMaterial( string path )
	{
		if ( !materials.TryGetValue( path, out var xml ) )
			materials[path] = xml = FileSystem.FileExists( path ) ? XmlAsset.Read( FileSystem, path, "material" ) : null;
		return xml;
	}

	internal string MaterialParticle( string surface, string action ) =>
		ReadMaterial( $"Data/Materials/{surface ?? "default"}.xml" )?.Element( "particles" )?.Element( action )?.Attribute( "path" );

	internal float MaterialHardness( string surface ) =>
		ReadMaterial( $"Data/Materials/{surface ?? "default"}.xml" )?.Element( "physics" )?.Number( "hardness" ) ?? 1f;

	internal bool AllowsPenetration( string surface ) => ReadMaterial( $"Data/Materials/{surface ?? "default"}.xml" )?.Element( "physics" )?.Number( "sharp_penetration" ) > 0f;

	internal void PlayMaterialEvent( CharacterLoader character, GameObject actor, string surface, string step, Vector3 position, float gain )
	{
		if ( surface is not null ) PlaySurfaceEvent( actor, surface, step, position, gain, character.SoundMod, true );
		if ( character.Clothing is { Length: > 0 } clothing )
			PlayEvent( ReadMaterial( clothing ), null, actor, step, position, gain, true );
	}

	internal void PlaySurfaceEvent( GameObject actor, string surface, string step, Vector3 position, float gain, string mod, bool useMaterialDistance = false )
	{
		PlayEvent( SurfaceMaterial( surface ), mod, actor, step, position, gain, useMaterialDistance );
	}

	XmlAsset SurfaceMaterial( string surface ) => ReadMaterial( $"Data/Materials/{surface ?? "default"}.xml" ) ?? ReadMaterial( "Data/Materials/default.xml" );

	static XmlAsset MaterialEvent( XmlAsset material, string mod, string step )
	{
		var events = material?.Elements( "events" );
		return events?.FirstOrDefault( e => e.Attribute( "mod" ) == mod )?.Element( step )
			?? events?.FirstOrDefault( e => e.Attribute( "mod" ) is null )?.Element( step );
	}

	internal void PlayItemImpact( GameObject item, string surface, string step, Vector3 position, float gain, string mod )
	{
		var entry = MaterialEvent( SurfaceMaterial( surface ), mod, step );
		if ( entry?.Attribute( "soundgroup" ) is not { } group ) return;
		var parent = entry.Attribute( "attached" ) == "true" ? item : null;
		float distance = item.Scene.Camera?.View.Position.Distance( position ) ?? 0f;
		float filtered = Math.Clamp( distance / 400f - 0.4f, 0f, 1f );
		if ( filtered < 1f ) PlayImpactLayer( 1f - filtered, false );
		if ( filtered > 0f ) PlayImpactLayer( filtered, true );

		void PlayImpactLayer( float maxGain, bool distant )
		{
			var handle = PlayGroupLayer( group, position, gain, parent, maxGain, distant );
			if ( handle.IsValid() ) handle.AirAbsorption = false;
		}
	}

	void PlayEvent( XmlAsset material, string mod, GameObject actor, string step, Vector3 position, float gain, bool useMaterialDistance )
	{
		var entry = MaterialEvent( material, mod, step );
		if ( entry?.Attribute( "soundgroup" ) is not { } group ) return;
		var handle = PlayGroup( group, position, gain, entry.Attribute( "attached" ) == "true" ? actor : null );
		if ( !handle.IsValid() ) return;
		if ( useMaterialDistance )
		{
			float distance = entry.Number( "max_distance" ) ?? ReadMaterial( "Data/Materials/base.xml" )?.Element( "events" )?.Element( step )?.Number( "max_distance" ) ?? 0f;
			if ( distance > 0 ) handle.Distance = distance * gain * 40f;
		}
	}

	internal (SoundFile File, float Volume, float Distance) PickSound( string path, bool voice = false, bool distant = false )
	{
		if ( !soundGroups.TryGetValue( path, out var group ) )
		{
			var xml = XmlAsset.Read( FileSystem, path, "soundgroup" );
			// Some shipped groups declare more takes than the game installs.
			var variants = Enumerable.Range( 1, (int)(xml.Number( "variants" ) ?? 1) )
				.Select( i => $"{path[..^4]}_{i}.wav" )
				.Where( file => FileSystem.FileExists( file ) )
				.Select( file => MountedPath( file ) + ".vsnd" )
				.ToArray();
			soundGroups[path] = group = (variants, xml.Number( "volume" ) ?? 1, (xml.Number( "max_distance" ) ?? 30f) * 40f);
		}
		if ( group.Variants.Length == 0 ) return (null, group.Volume, group.Distance);
		int variant = Random.Shared.Next( group.Variants.Length );
		if ( voice && group.Variants.Length > 1 )
		{
			if ( !voiceHistory.TryGetValue( path, out var recent ) ) voiceHistory[path] = recent = new();
			while ( recent.Contains( variant ) ) variant = Random.Shared.Next( group.Variants.Length );
			recent.Enqueue( variant );
			if ( recent.Count > group.Variants.Length / 2 ) recent.Dequeue();
		}
		string resource = group.Variants[variant];
		var file = distant && GetByPath( resource ) is SoundLoader loader ? loader.DistantSound : null;
		file ??= SoundFile.Load( resource );
		return (file, group.Volume, group.Distance);
	}

	internal SoundHandle PlayGroup( string path, Vector3 position, float gain = 1f, GameObject parent = null ) =>
		PlayGroupLayer( path, position, gain, parent, 1f, false );

	SoundHandle PlayGroupLayer( string path, Vector3 position, float gain, GameObject parent, float maxGain, bool distant )
	{
		var (file, volume, distance) = PickSound( path, distant: distant );
		if ( file is null ) return null;
		float level = volume * gain;
		var handle = Sound.PlayFile( file, Math.Min( maxGain, level ) );
		if ( handle.IsValid() )
		{
			ConfigureGroup( handle, level / maxGain, distance );
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

	internal static void ConfigureGroup( SoundHandle handle, float level, float distance )
	{
		handle.OcclusionEnabled = false;
		handle.MinDistance = 40f * Math.Max( 1f, level );
		handle.Distance = distance;
	}
}
