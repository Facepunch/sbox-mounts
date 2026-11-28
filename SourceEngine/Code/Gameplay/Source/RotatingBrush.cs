using System;
using System.Globalization;

namespace Source1;

[Hide]
public sealed class RotatingBrush : MovingBrush, IMapInput, Component.ICollisionListener
{
	[Property] public Angles Axis { get; set; }
	[Property] public float MaxSpeed { get; set; }
	[Property] public float Friction { get; set; }
	[Property] public bool StartOn { get; set; }
	[Property] public bool HurtTouch { get; set; }
	[Property] public SoundEvent Sound { get; set; }
	[Property] public float Volume { get; set; } = 1;
	[Sync( SyncFlags.FromHost )] public float Speed { get; private set; }

	Angles _home;
	float _angle, _target;
	bool _reverse, _returning, _playing;
	SoundHandle _sound;

	protected override void OnStart()
	{
		_home = LocalRotation.Angles();
		if ( StartOn && Networking.IsHost ) Start();
	}

	public void Start() => SetSpeed( _reverse ? -1 : 1 );
	public void Stop() { _target = 0; _returning = false; }
	public void Reverse() => SetSpeed( (_reverse ? 1 : -1) * MathF.Abs( Speed ) / MaxSpeed );
	public void Toggle() { if ( _target == 0 ) Start(); else Stop(); }
	public void SetSpeed( float fraction )
	{
		_target = fraction.Clamp( -1, 1 ) * MaxSpeed;
		_reverse = fraction < 0;
		_returning = false;
	}

	public void StopAtStart()
	{
		if ( Speed != 0 ) _returning = true;
		else SnapToStart();
	}

	public void SnapToStart()
	{
		Stop();
		Speed = _angle = 0;
		LocalRotation = _home.ToRotation();
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || (Speed == 0 && _target == 0) ) return;
		var goal = _returning ? Speed : _target;
		var reversing = Speed * goal < 0;
		var rate = Friction * MaxSpeed * (!reversing && MathF.Abs( Speed ) < MathF.Abs( goal ) ? 2 : 1);
		Speed = Friction <= 0 ? goal : Speed.Approach( reversing ? 0 : goal, rate * Time.Delta );
		var step = Speed * Time.Delta;
		if ( _returning && MathF.Abs( step ) >= (_angle * -MathF.Sign( step ) + 360) % 360 ) { SnapToStart(); return; }
		_angle = (_angle + step) % 360;
		LocalRotation = (_home + Axis * _angle).ToRotation();
	}

	protected override void OnUpdate()
	{
		if ( Sound is null || Application.IsDedicatedServer ) return;
		var moving = Speed != 0;
		if ( moving != _playing )
		{
			if ( moving ) _sound = GameObject.PlaySound( Sound );
			else _sound?.Stop();
			_playing = moving;
		}
		if ( !moving || _sound is null ) return;
		var fraction = MathF.Abs( Speed ) / MaxSpeed;
		_sound.Pitch = 0.3f + 0.7f * fraction;
		_sound.Volume = Volume * fraction;
	}

	protected override void OnDisabled() { _sound?.Stop(); _playing = false; }

	void Touch( GameObject other )
	{
		if ( !Networking.IsHost || !HurtTouch || Speed == 0 ) return;
		var damage = MathF.Abs( Speed ) * 0.1f;
		Crush( other, damage, other.WorldPosition );
		if ( other.GetComponent<Rigidbody>() is { } body ) body.Velocity = (other.WorldPosition - WorldPosition).Normal * damage;
	}

	void ICollisionListener.OnCollisionStart( Collision collision ) => Touch( collision.Other.GameObject );
	void ICollisionListener.OnCollisionUpdate( Collision collision ) => Touch( collision.Other.GameObject );

	Doo.Block IMapInput.BuildInput( string input, string parameter, bool hasUser )
	{
		switch ( input.ToLowerInvariant() )
		{
			case "start": return this.Call( nameof( Start ) );
			case "stop": return this.Call( nameof( Stop ) );
			case "toggle": return this.Call( nameof( Toggle ) );
			case "reverse": return this.Call( nameof( Reverse ) );
			case "stopatstartpos": return this.Call( nameof( StopAtStart ) );
			case "snaptostartpos": return this.Call( nameof( SnapToStart ) );
			case "startforward": return SpeedInput( 1 );
			case "startbackward": return SpeedInput( -1 );
			case "setspeed" when float.TryParse( parameter, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed ) && float.IsFinite( speed ): return SpeedInput( speed );
			default: return null;
		}
	}

	Doo.Block SpeedInput( float speed ) => this.Call( nameof( SetSpeed ), new Doo.LiteralExpression { LiteralValue = speed } );
}
