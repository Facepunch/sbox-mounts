using System;
using Sandbox;

namespace GoldSrc.Maps;

[Title( "GoldSrc Ambient Sound" ), Icon( "graphic_eq" )]
public sealed class AmbientSound : MapEntity
{
	[Property] public SoundEvent Sound { get; set; }
	[Property] public string Sentence { get; set; }
	[Property] public float Attenuation { get; set; }
	[Property] public bool OneShot { get; set; }
	[Property] public bool StartSilent { get; set; }
	[Property] public int Preset { get; set; }
	[Property] public int Pitch { get; set; }
	[Property] public int PitchStart { get; set; }
	[Property] public int SpinUp { get; set; }
	[Property] public int SpinDown { get; set; }
	[Property] public float Volume { get; set; } = 1f;
	[Property] public int VolumeStart { get; set; }
	[Property] public int FadeIn { get; set; }
	[Property] public int FadeOut { get; set; }
	[Property] public int LfoType { get; set; }
	[Property] public int LfoRate { get; set; }
	[Property] public int LfoPitch { get; set; }
	[Property] public int LfoVolume { get; set; }
	[Property] public int IncrementalSpinUp { get; set; }

	public bool IsPlaying { get; private set; }
	public int CurrentPitch => _pitch;
	public int CurrentVolume => _volume;

	private SoundHandle _handle;
	private int _pitch, _pitchRun, _pitchStart, _pitchFraction;
	private int _volume, _volumeRun, _volumeStart, _volumeFraction;
	private int _spinUp, _spinDown, _fadeIn, _fadeOut;
	private int _spinUpSaved, _spinDownSaved, _fadeInSaved, _fadeOutSaved;
	private int _lfoType, _lfoRate, _lfoPitch, _lfoVolume, _lfoFraction, _lfoMultiplier;
	private int _incrementalSpinUp, _spinCount;
	private bool _thinking;
	private TimeUntil _nextThink;

	protected override void OnStart()
	{
		InitializeModulation();
		if ( StartSilent || OneShot ) return;
		IsPlaying = true;
		Play();
	}

	protected override void OnDisabled()
	{
		IsPlaying = false;
		Stop();
	}

	public override void Trigger( GameObject activator = null ) => Toggle();
	public override void TurnOn( GameObject activator = null )
	{
		if ( !IsPlaying ) Toggle();
	}

	public override void TurnOff( GameObject activator = null )
	{
		if ( IsPlaying ) Toggle();
	}

	public override void SetValue( float value, GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		if ( !IsPlaying )
		{
			Toggle();
			return;
		}
		var fraction = value > 1f ? 1f : value < 0f ? 0.01f : value;
		_pitch = (int)(fraction * 255f);
		if ( _handle.IsValid() )
			_handle.Pitch = (_pitch == 0 ? 100 : _pitch) / 100f;
	}

	private void Toggle()
	{
		if ( !CanTrigger ) return;
		if ( !IsPlaying )
		{
			IsPlaying = !OneShot;
			InitializeModulation();
			Play();
			return;
		}
		if ( _incrementalSpinUp != 0 )
		{
			if ( _spinCount > _incrementalSpinUp ) return;
			_spinCount++;
			_spinUp = _spinUpSaved;
			_spinDown = 0;
			_pitchRun = Math.Min( 255, _pitchStart + (255 - _pitchStart) / _incrementalSpinUp * _spinCount );
			_thinking = true;
			_nextThink = 0.1f;
			return;
		}
		IsPlaying = false;
		StartSilent = true;
		if ( _spinDownSaved == 0 && _fadeOutSaved == 0 )
		{
			Stop();
			return;
		}
		_spinDown = _spinDownSaved;
		_spinUp = 0;
		_fadeOut = _fadeOutSaved;
		_fadeIn = 0;
		_thinking = true;
		_nextThink = 0.1f;
	}

	private void InitializeModulation()
	{
		var preset = Preset is >= 1 and <= 27 ? Presets[Preset - 1] : null;
		_pitchRun = preset?[0] ?? Math.Clamp( Pitch, 0, 255 );
		_pitchStart = preset?[1] ?? Math.Clamp( PitchStart, 0, 255 );
		_spinUpSaved = Ramp( preset?[2] ?? SpinUp );
		_spinDownSaved = Ramp( preset?[3] ?? SpinDown );
		_volumeRun = preset is null ? Math.Clamp( (int)(Volume * 100), 0, 100 ) : preset[4] * 10;
		_volumeStart = Math.Clamp( preset?[5] ?? VolumeStart, 0, 10 ) * 10;
		_fadeInSaved = Ramp( preset?[6] ?? FadeIn );
		_fadeOutSaved = Ramp( preset?[7] ?? FadeOut );
		_lfoType = preset?[8] ?? (LfoType > 4 ? 2 : LfoType);
		_lfoRate = Math.Clamp( preset?[9] ?? LfoRate, 0, 1000 ) * 256;
		_lfoPitch = Math.Clamp( preset?[10] ?? LfoPitch, 0, 100 );
		_lfoVolume = Math.Clamp( preset?[11] ?? LfoVolume, 0, 100 );
		_incrementalSpinUp = Math.Clamp( preset?[12] ?? IncrementalSpinUp, 0, 100 );
		_spinUp = _spinUpSaved;
		_spinDown = 0;
		_fadeIn = _fadeInSaved;
		_fadeOut = 0;
		_pitch = _spinUp != 0 ? _pitchStart : _pitchRun;
		if ( _pitch == 0 ) _pitch = 100;
		_volume = _fadeIn != 0 ? _volumeStart : _volumeRun;
		_pitchFraction = _pitch << 8;
		_volumeFraction = _volume << 8;
		_lfoFraction = 0;
		if ( preset is not null ) _lfoMultiplier = 0;
		_spinCount = 1;
		if ( _incrementalSpinUp != 0 )
			_pitchRun = Math.Min( 255, _pitchStart + (255 - _pitchStart) / _incrementalSpinUp );
		if ( (_spinUpSaved != 0 || _spinDownSaved != 0 || (_lfoType != 0 && _lfoPitch != 0)) && _pitch == 100 )
			_pitch = 101;
	}

	private static int Ramp( int value )
	{
		value = Math.Clamp( value, 0, 100 );
		return value > 0 ? (101 - value) * 64 : 0;
	}

	private void Play()
	{
		Stop();
		if ( Sentence is not null && Networking.IsHost && Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			mount.NpcAudio.Emit( Sentence, WorldPosition, _volume / 100f, _pitch / 100f, attenuation: Attenuation );
		if ( Sound is not null )
		{
			_handle = global::Sandbox.Sound.Play( Sound, WorldPosition );
			if ( _handle.IsValid() )
			{
				_handle.Volume = _volume / 100f;
				_handle.Pitch = _pitch / 100f;
			}
		}
		_thinking = true;
		_nextThink = 0.1f;
	}

	private void Stop()
	{
		_handle?.Stop();
		_handle = null;
		_thinking = false;
	}

	protected override void OnUpdate()
	{
		if ( !_thinking || _nextThink > 0 ) return;
		if ( _spinUp == 0 && _spinDown == 0 && _fadeIn == 0 && _fadeOut == 0 && _lfoType == 0 )
		{
			_thinking = false;
			return;
		}
		var pitch = _pitch;
		var volume = _volume;
		var pitchChanged = false;
		var volumeChanged = false;
		var changed = false;
		if ( _spinUp != 0 || _spinDown != 0 )
		{
			var previous = _pitchFraction >> 8;
			_pitchFraction += _spinUp > 0 ? _spinUp : -_spinDown;
			pitch = _pitchFraction >> 8;
			if ( pitch > _pitchRun ) { pitch = _pitchRun; _spinUp = 0; }
			if ( pitch < _pitchStart ) { _spinDown = 0; Stop(); return; }
			_pitch = pitch = Math.Clamp( pitch, 1, 255 );
			changed |= previous != pitch;
			pitchChanged = true;
		}
		if ( _fadeIn != 0 || _fadeOut != 0 )
		{
			var previous = _volumeFraction >> 8;
			_volumeFraction += _fadeIn > 0 ? _fadeIn : -_fadeOut;
			volume = _volumeFraction >> 8;
			if ( volume > _volumeRun ) { volume = _volumeRun; _fadeIn = 0; }
			if ( volume < _volumeStart ) { _fadeOut = 0; Stop(); return; }
			_volume = volume = Math.Clamp( volume, 1, 100 );
			changed |= previous != volume;
			volumeChanged = true;
		}
		if ( _lfoType != 0 )
		{
			if ( _lfoFraction > 0x6fffffff ) _lfoFraction = 0;
			_lfoFraction += _lfoRate;
			var position = _lfoFraction >> 8;
			if ( _lfoFraction < 0 )
			{
				_lfoFraction = 0;
				_lfoRate = Math.Abs( _lfoRate );
				position = 0;
			}
			else if ( position > 255 )
			{
				position = 255;
				_lfoFraction = 255 << 8;
				_lfoRate = -Math.Abs( _lfoRate );
			}
			_lfoMultiplier = _lfoType switch
			{
				1 => position < 128 ? 255 : 0,
				3 => position == 255 ? Game.Random.Int( 0, 255 ) : _lfoMultiplier,
				_ => position
			};
			if ( _lfoPitch != 0 )
			{
				var previous = pitch;
				pitch = Math.Clamp( pitch + (_lfoMultiplier - 128) * _lfoPitch / 100, 1, 255 );
				changed |= previous != pitch;
				pitchChanged = true;
			}
			if ( _lfoVolume != 0 )
			{
				var previous = volume;
				volume = Math.Clamp( volume + (_lfoMultiplier - 128) * _lfoVolume / 100, 0, 100 );
				changed |= previous != volume;
				volumeChanged = true;
			}
		}
		if ( changed && _handle.IsValid() )
		{
			if ( pitchChanged ) _handle.Pitch = (pitch == 100 ? 101 : pitch) / 100f;
			if ( volumeChanged ) _handle.Volume = volume / 100f;
		}
		_nextThink = 0.2f;
	}

	private static readonly int[][] Presets =
	[
		[255,75,95,95,10,1,50,95,0,0,0,0,0],
		[255,85,70,88,10,1,20,88,0,0,0,0,0],
		[255,100,50,75,10,1,10,75,0,0,0,0,0],
		[100,100,0,0,10,1,90,90,0,0,0,0,0],
		[100,100,0,0,10,1,80,80,0,0,0,0,0],
		[100,100,0,0,10,1,50,70,0,0,0,0,0],
		[100,100,0,0,5,1,40,50,1,50,0,10,0],
		[100,100,0,0,5,1,40,50,1,150,0,10,0],
		[100,100,0,0,5,1,40,50,1,750,0,10,0],
		[128,100,50,75,10,1,30,40,2,8,20,0,0],
		[128,100,50,75,10,1,30,40,2,25,20,0,0],
		[128,100,50,75,10,1,30,40,2,70,20,0,0],
		[50,50,0,0,10,1,20,50,0,0,0,0,0],
		[70,70,0,0,10,1,20,50,0,0,0,0,0],
		[90,90,0,0,10,1,20,50,0,0,0,0,0],
		[120,120,0,0,10,1,20,50,0,0,0,0,0],
		[180,180,0,0,10,1,20,50,0,0,0,0,0],
		[255,255,0,0,10,1,20,50,0,0,0,0,0],
		[200,75,90,90,10,1,50,90,2,100,20,0,0],
		[255,75,97,90,10,1,50,90,1,40,50,0,0],
		[100,100,0,0,10,1,30,50,3,15,20,0,0],
		[160,160,0,0,10,1,50,50,3,500,25,0,0],
		[255,75,88,0,10,1,40,0,0,0,0,0,5],
		[200,20,95,70,10,1,70,70,3,20,50,0,0],
		[180,100,50,60,10,1,40,60,2,90,100,100,0],
		[60,60,0,0,10,1,40,70,3,80,20,50,0],
		[128,90,10,10,10,1,20,40,1,5,10,20,0]
	];
}
