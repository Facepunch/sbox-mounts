using System;
using Sandbox.Audio;

namespace NS2;

public abstract class SparkSoundEmitter : Component
{
	[Property] public string EventName { get; set; }
	[Property] public bool Positional { get; set; }
	[Property] public float Pitch { get; set; }
	protected virtual float VolumeScale => 1;
	protected virtual float Gain( Vector3 position ) => definition.DistanceGain( position.Distance( Sound.Listener.Position ) / MapLoader.Scale );
	protected abstract void UpdatePlayback();

	SoundProject.Event definition;
	LayerPlayer[] layers;
	protected bool Playing { get; private set; }
	float startTime, eventPitch;

	protected override void OnStart()
	{
		if ( Application.IsDedicatedServer ) return;
		var mount = Sandbox.Mounting.Directory.Get( "ns2" ) as GameMount;
		definition = mount?.FindSoundEvent( EventName );
		if ( definition is null )
			Log.Warning( $"NS2 sound event '{EventName}' is unavailable." );
	}

	protected override void OnUpdate()
	{
		if ( definition is null ) return;
		UpdatePlayback();
		if ( layers is null ) return;
		bool active = false;
		foreach ( var layer in layers )
		{
			layer.Update( this );
			active |= !layer.Finished;
		}
		if ( !active ) ReleaseVoices();
	}

	protected void StartEvent()
	{
		Playing = true;
		if ( definition.Limit > 0 )
		{
			int count = 0;
			SparkSoundEmitter victim = null;
			foreach ( var other in Scene.GetAllComponents<SparkSoundEmitter>() )
			{
				if ( !ReferenceEquals( other.definition, definition ) || other.layers is null ) continue;
				count++;
				if ( victim is null || (definition.Steal is 3 or 5
					? other.Gain( other.WorldPosition ) < victim.Gain( victim.WorldPosition )
					: other.startTime < victim.startTime) ) victim = other;
			}
			if ( count >= definition.Limit )
			{
				if ( definition.Steal == 4 || (definition.Steal == 5 && Gain( WorldPosition ) <= victim.Gain( victim.WorldPosition )) ) return;
				victim.ReleaseVoices(); // Spark keeps the trigger latched after FMOD steals the event.
			}
		}

		startTime = Time.Now;
		eventPitch = Pitch + definition.Pitch + Random.Shared.Float( -definition.PitchRandom, definition.PitchRandom );
		layers = Array.ConvertAll( definition.Layers, l => new LayerPlayer( l ) );
	}

	void ReleaseVoices()
	{
		if ( layers is null ) return;
		foreach ( var layer in layers ) layer.Stop( definition.FadeOut );
		layers = null;
	}

	protected void StopEvent()
	{
		ReleaseVoices();
		Playing = false;
	}

	protected override void OnDisabled()
	{
		StopEvent();
	}

	sealed class LayerPlayer( SoundProject.Layer layer )
	{
		readonly record struct Voice( SoundHandle Handle, float Gain, float Pitch, Vector3 Offset );
		readonly List<Voice> voices = [];
		readonly SoundProject.Settings settings = layer.Playlist.Settings;
		TimeUntil next = Random.Shared.Float( layer.Playlist.Settings.DelayMin, layer.Playlist.Settings.DelayMax );
		int last = -1, spawned, shuffleIndex;
		int[] shuffled;
		bool ended;
		Mixer mixer;
		LowPassProcessor lowPass;
		AmbientDistortion distortion;
		internal bool Finished => ended && voices.Count == 0;

		internal void Stop( float fade )
		{
			foreach ( var voice in voices )
			{
				if ( mixer is not null ) voice.Handle.TargetMixer = null;
				voice.Handle.Stop( fade );
			}
			mixer?.Destroy();
			mixer = null;
			voices.Clear();
		}

		void ApplyMix( SparkSoundEmitter owner, Voice voice )
		{
			var h = voice.Handle;
			var position = owner.WorldPosition + voice.Offset;
			if ( h.Position != position ) h.Position = position;
			float gain = voice.Gain, pitch = voice.Pitch;
			float pan = owner.definition.Pan, reverb = owner.definition.Reverb;
			float meters = h.Position.Distance( Sound.Listener.Position ) / MapLoader.Scale;
			foreach ( var env in layer.Envelopes )
			{
				float v = env.Evaluate( env.ParameterName == "(distance)" ? meters : env.Min );
				switch ( env.Effect )
				{
					case SoundProject.Effect.Volume: gain *= v; break;
					case SoundProject.Effect.Pitch: pitch *= MathF.Pow( 2, 8 * v - 4 ); break;
					case SoundProject.Effect.Reverb: reverb = v; break;
					case SoundProject.Effect.Pan: pan = v; break;
					case SoundProject.Effect.Occlusion: h.OcclusionEnabled = v > 0; break;
					case SoundProject.Effect.LowPass:
						if ( lowPass is null ) { lowPass = new(); GetMixer().AddProcessor( lowPass ); }
						lowPass.Frequency = 20 * MathF.Pow( 1000, v );
						break;
					case SoundProject.Effect.Distortion:
						if ( distortion is null ) { distortion = new(); GetMixer().AddProcessor( distortion ); }
						distortion.Level = v;
						break;
				}
			}
			h.TargetMixer = mixer;
			h.Volume = owner.VolumeScale * gain * owner.Gain( h.Position );
			h.SpacialBlend = owner.Positional ? pan : 0;
			h.Reverb = reverb;
			h.Pitch = pitch;
		}

		Mixer GetMixer()
		{
			if ( mixer is not null ) return mixer;
			mixer = Mixer.Default.AddChild();
			mixer.Name = $"NS2 ambient {mixer.Id}";
			return mixer;
		}

		internal void Update( SparkSoundEmitter owner )
		{
			for ( int i = voices.Count - 1; i >= 0; i-- )
			{
				var v = voices[i];
				if ( !v.Handle.IsPlaying ) voices.RemoveAt( i );
				else ApplyMix( owner, v );
			}
			if ( ended || next > 0 ) return;
			bool periodic = settings.IntervalMax > 0;
			if ( voices.Count >= (periodic ? Math.Max( 1, settings.Polyphony ) : 1) ) return;
			var waves = layer.Playlist.Waves;
			if ( waves.Length == 0 ) { ended = true; return; }
			var wave = waves[Choose( waves )];
			bool continuous = layer.LoopsWave;
			var path = $"mount://ns2/{wave.Path}.vsnd";
			var file = SoundFile.Load( continuous ? $"{path}/loop.vsnd" : path );
			if ( file is null || !file.IsValidForPlayback ) { ended = true; return; }

			var pos = owner.WorldPosition;
			if ( settings.ScatterMax > 0 ) pos += Vector3.Random.Normal * (MapLoader.Scale * Random.Shared.Float( settings.ScatterMin, settings.ScatterMax ));
			float gain = owner.definition.Volume * layer.Volume * settings.Volume * Random.Shared.Float( settings.VolumeRandom, 1 );
			float pitch = owner.eventPitch + settings.Pitch + layer.Pitch + Random.Shared.Float( -settings.PitchRandom, settings.PitchRandom );
			var handle = Sound.PlayFile( file, gain * owner.Gain( pos ), MathF.Pow( 2, pitch ), fadeInTime: spawned == 0 ? owner.definition.FadeIn : 0 );
			if ( !handle.IsValid() ) { ended = true; return; }
			handle.Name = owner.EventName;
			handle.Position = pos;
			handle.SpacialBlend = owner.Positional ? 1 : 0;
			handle.ListenLocal = !owner.Positional;
			handle.DistanceAttenuation = false;
			handle.AirAbsorption = false;
			handle.OcclusionEnabled = false;
			var voice = new Voice( handle, gain, handle.Pitch, pos - owner.WorldPosition );
			ApplyMix( owner, voice );
			voices.Add( voice );
			spawned++;
			ended = continuous || (!periodic && !layer.Loop) || (settings.SpawnCount > 0 && spawned >= settings.SpawnCount);
			if ( periodic ) next = Random.Shared.Float( settings.IntervalMin, settings.IntervalMax );
		}

		int Choose( SoundProject.Wave[] waves )
		{
			if ( waves.Length == 1 ) return 0;
			if ( settings.Order is 8 or 9 ) return last = (last + 1) % waves.Length;
			if ( settings.Order == 4 )
			{
				shuffled ??= Enumerable.Range( 0, waves.Length ).ToArray();
				if ( shuffleIndex == 0 ) Random.Shared.Shuffle( shuffled.AsSpan() );
				int index = shuffled[shuffleIndex];
				shuffleIndex = (shuffleIndex + 1) % waves.Length;
				return index;
			}
			int skip = settings.Order == 0 ? last : -1;
			int weight = 0, selected = 0;
			for ( int i = 0; i < waves.Length; i++ )
			{
				if ( i == skip || waves[i].Weight <= 0 ) continue;
				weight += waves[i].Weight;
				if ( Random.Shared.Next( weight ) < waves[i].Weight ) selected = i;
			}
			return last = selected;
		}
	}

	// Approximate the Designer distortion effect with a normalized soft clip.
	sealed class AmbientDistortion : AudioProcessor
	{
		internal float Level;
		protected override void ProcessSingleChannel( AudioChannel channel, Span<float> input )
		{
			float level = Math.Clamp( Level, 0, 0.99f );
			float drive = 2 * level / (1 - level);
			for ( int i = 0; i < input.Length; i++ )
				input[i] = (1 + drive) * input[i] / (1 + drive * MathF.Abs( input[i] ));
		}
	}
}
