using System;
using Sandbox.Npcs.Layers;
using Sandbox.Npcs;

namespace Source1;

/// <summary>
/// Speaks an NPC's line, a sound script's wave or a sentence's waves back to back, from the speaker's eyes. The line holds the
/// NPC's speech, so a higher priority line, the NPC's death or its removal cuts it off; a death line outlives its speaker.
/// </summary>
[Title( "Source Sentence Player" )]
[Category( "Source 1" )]
public sealed class SourceSentencePlayer : Component
{
	/// <summary>A line without a sound level (a talker response) carries as SNDLVL_TALKING (80 dB): full volume up close, halving with each doubling of distance.</summary>
	const float TalkingMinDistance = 250;
	const float TalkingDistance = 6240;

	string[] _waves = [];
	float[] _pitch = [];
	float[] _volume = [];
	SoundEvent _level;
	int _next;
	bool _oneShot;
	SoundHandle _handle;

	/// <summary>Host only: the speech this line holds; when the NPC's speech changes, the line was cut off.</summary>
	string _speech;

	/// <summary>
	/// Speaks the waves (mount paths) as the NPC's speech, on the host; <paramref name="level"/> is the path of the sound script
	/// whose sound level they carry at. False when the speech layer refuses the line.
	/// </summary>
	public static bool TrySay( BaseNpc npc, string name, string[] waves, float[] pitch, float[] volume, string level, GameObject lookAt, string tag, int priority )
	{
		var duration = 0.2f;
		for ( var i = 0; i < waves.Length; i++ )
		{
			var file = SoundFile.Load( waves[i] );
			duration += (file.IsValid() && file.IsLoaded ? file.Duration / Math.Max( pitch[i], 0.1f ) : 0.6f) + 0.05f;
		}

		if ( !npc.Speech.TrySay( name, new SpeechOptions { Duration = duration, LookAt = lookAt, Tags = [tag], Priority = priority } ) )
			return false;

		Play( npc.GameObject, waves, pitch, volume, level );
		var player = npc.GetOrAddComponent<SourceSentencePlayer>();
		player.Flags |= ComponentFlags.NotNetworked;
		player._speech = name;
		return true;
	}

	/// <summary>Cuts the dying NPC's line and speaks its death line from where it fell, finishing after the NPC is gone.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	public static void SayDeath( GameObject speaker, Vector3 position, string[] waves, float[] pitch, float[] volume, string level )
	{
		if ( speaker.IsValid() && speaker.GetComponent<SourceSentencePlayer>() is { } current )
			current.Stop();

		var go = new GameObject( true, "Source Sentence" );
		go.WorldPosition = position;
		var player = go.AddComponent<SourceSentencePlayer>();
		player._oneShot = true;
		player.Begin( waves, pitch, volume, level );
	}

	/// <summary>Where the NPC speaks from, above its origin.</summary>
	public static Vector3 Mouth( BaseNpc npc ) => Vector3.Up * (npc.IsValid() ? npc.EyeHeight : 0);

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Play( GameObject speaker, string[] waves, float[] pitch, float[] volume, string level )
	{
		if ( !speaker.IsValid() )
			return;

		var player = speaker.GetOrAddComponent<SourceSentencePlayer>();
		player.Flags |= ComponentFlags.NotNetworked;
		player.Begin( waves, pitch, volume, level );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Cut( GameObject speaker )
	{
		if ( speaker.IsValid() && speaker.GetComponent<SourceSentencePlayer>() is { } player )
			player.Stop();
	}

	void Begin( string[] waves, float[] pitch, float[] volume, string level )
	{
		Stop();
		_waves = waves;
		_pitch = pitch;
		_volume = volume;
		_level = string.IsNullOrEmpty( level ) ? null : GameResource.Load<SoundEvent>( level );
		_next = 0;
	}

	void Stop()
	{
		_handle?.Stop();
		_waves = [];
		_speech = null;
	}

	protected override void OnUpdate()
	{
		if ( _speech is not null && Networking.IsHost )
		{
			// The speech layer ends a line at its estimated length, which is short for a wave that was not loaded yet; that is not a cut.
			var speaker = GetComponent<BaseNpc>();
			var current = speaker?.Speech?.CurrentSpeech;
			if ( current is null && speaker.IsValid() && speaker.Health >= 1 )
			{
				_speech = null;
			}
			else if ( current != _speech )
			{
				Cut( GameObject );
				return;
			}
		}

		if ( _handle.IsValid() )
			return;

		if ( _next >= _waves.Length )
		{
			_waves = [];
			_speech = null;
			if ( _oneShot )
				GameObject.Destroy();
			return;
		}

		var file = SoundFile.Load( _waves[_next] );
		var pitch = _pitch[_next];
		var volume = _volume[_next];
		_next++;
		if ( !file.IsValid() )
			return;

		var npc = GetComponent<BaseNpc>();
		_handle = npc?.Renderer is { } renderer && renderer.IsValid() ? renderer.SpeakSound( file, volume, pitch, GameObject ) : Sound.PlayFile( file, volume, pitch );
		if ( !_handle.IsValid() )
			return;

		_handle.Parent = GameObject;
		_handle.FollowParent = true;
		_handle.LocalTransform = new Transform( Mouth( npc ) );
		_handle.Position = WorldTransform.PointToWorld( _handle.LocalTransform.Position );
		Attenuate( _handle, _level );
	}

	/// <summary>The line carries as its sound script's sound level does, or as talking when it has none.</summary>
	static void Attenuate( SoundHandle handle, SoundEvent level )
	{
		if ( level is null || level.UI || !level.DistanceAttenuation )
		{
			handle.FalloffMode = SoundFalloffMode.Physical;
			handle.MinDistance = TalkingMinDistance;
			handle.Distance = TalkingDistance;
			return;
		}

		handle.Distance = level.Distance;
		handle.MinDistance = level.MinDistance;
		handle.Falloff = level.Falloff;
		handle.FalloffMode = level.FalloffMode;
		handle.AirAbsorption = level.AirAbsorption;
		handle.OcclusionEnabled = level.OcclusionEnabled;
		handle.ReverbEnabled = level.ReverbEnabled;
	}

	protected override void OnDisabled() => Stop();

	protected override void OnDestroy() => Stop();
}
