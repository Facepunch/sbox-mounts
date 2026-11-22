using System;
using Sandbox;

namespace GoldSrc.Maps;

[Title( "GoldSrc Rotating Brush" )]
[Category( "GoldSrc" )]
public sealed class RotatingBrush : MapEntity, Component.ICollisionListener
{
	[Property] public Angles Axis { get; set; } = new( 0f, 1f, 0f );
	[Property] public Angles StartAngles { get; set; }
	[Property] public float Speed { get; set; }
	[Property] public float Friction { get; set; } = 1f;
	[Property] public bool Accelerate { get; set; }
	[Property] public bool StartOn { get; set; }
	[Property] public bool HurtTouch { get; set; }
	[Property] public float Damage { get; set; }
	[Property] public float Volume { get; set; } = 1f;
	[Property] public SoundEvent MoveSound { get; set; }
	public float AngularSpeed { get; private set; }

	private Angles _angles;
	private int _ramp;
	private float _nextThink = float.PositiveInfinity;
	private bool _starting;
	private SoundHandle _sound;

	protected override void OnStart()
	{
		_angles = StartAngles;
		WorldRotation = Rotation.From( _angles );
		_starting = StartOn;
		if ( StartOn ) _nextThink = Time.Now + 1.5f;
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		_starting = false;
		if ( AngularSpeed != 0f )
		{
			_ramp = -1;
			_nextThink = Time.Now + 0.1f;
			return;
		}
		PlaySound();
		if ( Accelerate )
		{
			_ramp = 1;
			_nextThink = Time.Now + 0.1f;
			if ( _sound.IsValid() )
			{
				_sound.Volume = 0.01f;
				_sound.Pitch = 0.3f;
			}
		}
		else
		{
			AngularSpeed = Speed;
			_ramp = 0;
			_nextThink = float.PositiveInfinity;
			UpdateSound( false );
		}
	}

	public override void TurnOn( GameObject activator = null ) => Trigger( activator );
	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	protected override void OnFixedUpdate()
	{
		if ( !CanTrigger ) return;
		_angles += Axis * (AngularSpeed * Time.Delta);
		WorldRotation = Rotation.From( _angles );
		if ( _sound.IsValid() ) _sound.Position = WorldPosition;
		if ( Time.Now < _nextThink ) return;
		if ( _starting )
		{
			Trigger();
			return;
		}
		if ( _ramp == 0 ) return;
		_nextThink = Time.Now + 0.1f;
		AngularSpeed += _ramp * Speed * Friction;
		var arrived = _ramp > 0 ? AngularSpeed >= Speed : AngularSpeed <= 0f;
		if ( arrived )
		{
			AngularSpeed = _ramp > 0 ? Speed : 0f;
			_ramp = 0;
			_nextThink = float.PositiveInfinity;
		}
		UpdateSound( !arrived );
	}

	private void PlaySound()
	{
		if ( MoveSound is not null && !_sound.IsValid() ) _sound = Sound.Play( MoveSound, WorldPosition );
	}

	private void UpdateSound( bool ramp )
	{
		if ( !_sound.IsValid() ) return;
		if ( AngularSpeed == 0f )
		{
			_sound.Stop();
			_sound = null;
			return;
		}
		var fraction = Speed > 0f ? AngularSpeed / Speed : 0f;
		var pitch = (int)(30f + 70f * fraction);
		if ( ramp && pitch == 100 ) pitch = 99;
		_sound.Pitch = pitch / 100f;
		_sound.Volume = Volume * fraction;
	}

	public void Blocked( GameObject other ) => BrushInteraction.Damage( GameObject, other, Damage );
	void ICollisionListener.OnCollisionStart( Collision collision ) => Touch( collision.Other.GameObject );
	void ICollisionListener.OnCollisionUpdate( Collision collision ) => Touch( collision.Other.GameObject );

	private void Touch( GameObject other )
	{
		if ( !CanTrigger || !HurtTouch || !other.IsValid() ) return;
		Damage = MathF.Abs( AngularSpeed ) * 0.1f;
		BrushInteraction.Damage( GameObject, other, Damage );
		BrushInteraction.SetVelocity( other, (other.WorldPosition - WorldPosition).Normal * Damage );
	}

	protected override void OnDisabled()
	{
		_sound?.Stop();
		_sound = null;
	}
}
