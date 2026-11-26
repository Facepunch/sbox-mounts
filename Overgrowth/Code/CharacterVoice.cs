using System;

namespace Overgrowth;

internal sealed class CharacterVoice
{
	VoiceTrack track;
	int frame;
	float time;
	SoundHandle handle;
	internal bool Playing => handle?.IsPlaying == true;

	internal static void Select( SkinnedModelRenderer renderer, string cue, Action<string, float, float, float> play )
	{
		if ( !renderer.IsValid() || Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ||
			mount.GetByPath( renderer.Model.ResourcePath ) is not CharacterLoader character ) return;
		var (group, pitch) = character.GetVoice( cue );
		if ( group is null ) return;
		var (file, volume, distance) = mount.PickSound( group, voice: true );
		if ( file is not null ) play( file.ResourcePath, volume, pitch, distance );
	}

	internal void Play( SkinnedModelRenderer renderer, Vector3 head, string path, float volume, float pitch, float distance )
	{
		track = null;
		handle = renderer.SpeakSound( SoundFile.Load( path ), Math.Min( 1f, volume ), pitch );
		if ( !handle.IsValid() ) return;
		handle.Position = head;
		handle.LocalTransform = new Transform( renderer.WorldTransform.PointToLocal( head ) );
		GameMount.ConfigureGroup( handle, volume, distance );
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount &&
			mount.GetByPath( path ) is SoundLoader sound && mount.GetByPath( renderer.Model.ResourcePath ) is CharacterLoader character &&
			character.GetVoiceTrack( sound ) is { } recording )
		{
			track = recording;
			frame = 0;
			time = recording.Frames[0].Time;
		}
	}

	internal void ApplyMouth( SkinnedModelRenderer renderer, float dt )
	{
		if ( track is null ) return;
		while ( frame < track.Frames.Length && track.Frames[frame].Time < time ) frame++;
		if ( frame == track.Frames.Length ) { track = null; return; }
		for ( int i = 0; i < track.Shapes.Length; i++ )
		{
			var name = track.Shapes[i];
			renderer.Morphs.Set( name, (renderer.Morphs.Get( name ) + track.Frames[frame].Weights[i]) * 0.5f, 0f );
		}
		time += dt;
	}

	internal void Stop()
	{
		handle?.Stop();
		track = null;
	}
}
