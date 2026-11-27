using System;

namespace Source1.Hl2;

/// <summary>
/// A looping sound on one peer whose volume and pitch glide to targets in steps, like a breath that quickens or a moan
/// that swells and fades. It plays on while silent, so a swell opens wherever the loop has got to.
/// </summary>
sealed class SoundLoop
{
	readonly GameObject _owner;
	readonly string _name;
	readonly bool _waitUntilHeard, _relative;
	readonly Ramp _volume, _pitch;
	SoundEvent _sound;
	SoundHandle _handle;
	float _baseVolume = 1;

	/// <summary>A value gliding through queued steps, each a target and the seconds to reach it.</summary>
	sealed class Ramp
	{
		public float Value;
		public Ramp( float value ) => Value = value;
		float _from, _to, _length, _time;
		readonly Queue<(float To, float Seconds)> _steps = new();

		public void Set( params (float To, float Seconds)[] steps )
		{
			_steps.Clear();
			foreach ( var step in steps )
				_steps.Enqueue( step );
			_time = _length = 0;
			Next();
		}

		void Next()
		{
			if ( !_steps.TryDequeue( out var step ) )
				return;

			_from = Value;
			_to = step.To;
			_length = MathF.Max( step.Seconds, 0 );
			_time = 0;
			if ( _length <= 0 )
			{
				Value = _to;
				Next();
			}
		}

		public void Tick( float delta )
		{
			if ( _time >= _length )
			{
				Next();
				return;
			}

			_time += delta;
			Value = MathX.Lerp( _from, _to, Math.Clamp( _time / _length, 0, 1 ) );
			if ( _time >= _length )
				Next();
		}
	}

	/// <param name="owner">The object the sound plays from; its mount supplies the sound.</param>
	/// <param name="name">The sound's name in the mount.</param>
	/// <param name="volume">Starting volume, 0 to 1.</param>
	/// <param name="pitch">Starting pitch, in percent of normal.</param>
	/// <param name="waitUntilHeard">Starts the sound, or starts it again after a wave without loop points has run out, only once it can be heard.</param>
	/// <param name="relative">Volumes scale the sound's own volume instead of replacing it.</param>
	public SoundLoop( GameObject owner, string name, float volume = 0, float pitch = 100, bool waitUntilHeard = false, bool relative = false )
	{
		_owner = owner;
		_name = name;
		_waitUntilHeard = waitUntilHeard;
		_relative = relative;
		_volume = new Ramp( volume );
		_pitch = new Ramp( pitch );
	}

	/// <summary>Volume steps, 0 to 1; returns how long they take.</summary>
	public float FadeVolume( params (float To, float Seconds)[] steps )
	{
		_volume.Set( steps );
		return steps.Sum( x => x.Seconds );
	}

	/// <summary>Pitch steps, in percent of normal.</summary>
	public void FadePitch( params (float To, float Seconds)[] steps ) => _pitch.Set( steps );

	public void Update()
	{
		if ( Application.IsDedicatedServer || !_owner.IsValid() )
			return;

		// Looked up on first use, once the owner's model says which mount it comes from
		_sound ??= Sounds.Sound( _name, MountContent.Of( _owner ) );
		if ( _sound is null )
			return;

		_volume.Tick( Time.Delta );
		_pitch.Tick( Time.Delta );

		// Started again when a wave without loop points has run out
		if ( (_handle is null || !_handle.IsValid() || _handle.IsStopped) && (!_waitUntilHeard || _volume.Value > 0.01f) )
		{
			_handle = _owner.PlaySound( _sound );
			_baseVolume = _handle is not null && _handle.IsValid() ? _handle.Volume : 1;
		}
		if ( _handle is null || !_handle.IsValid() )
			return;

		_handle.Volume = _volume.Value * (_relative ? _baseVolume : 1);
		_handle.Pitch = _pitch.Value / 100;
	}

	public void Stop()
	{
		if ( _handle is not null && _handle.IsValid() )
			_handle.Stop( 0.1f );
		_handle = null;
	}
}
