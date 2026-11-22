namespace GoldSrc.HalfLife;

[Hide]
public abstract class VoicedNpc : Npc
{
	SoundHandle _voice;
	protected float VoiceAmplitude => _voice.IsValid() ? _voice.Amplitude.Clamp( 0, 1 ) : 0;
	internal int VoiceSerial { get; private set; }

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal void StopVoice() => BeginVoice();

	internal int BeginVoice()
	{
		if ( _voice.IsValid() ) _voice.Stop();
		_voice = null;
		return ++VoiceSerial;
	}

	internal void TrackVoice( SoundHandle handle )
	{
		_voice = handle;
		if ( !handle.IsValid() || Health <= 0 ) return;
		handle.Parent = GameObject;
		handle.LocalTransform = global::Transform.Zero;
		handle.FollowParent = true;
	}
}
