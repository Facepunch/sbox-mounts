using System;
using Sandbox;

namespace GoldSrc.Maps;

[Title( "GoldSrc Pendulum" )]
[Category( "GoldSrc" )]
public sealed class PendulumBrush : MapEntity, Component.ICollisionListener
{
	[Property] public Angles Axis { get; set; } = new( 0f, 1f, 0f );
	[Property] public Angles StartAngles { get; set; }
	[Property] public float Distance { get; set; }
	[Property] public float MaxSpeed { get; set; } = 100f;
	[Property] public float Damping { get; set; }
	[Property] public float Damage { get; set; }
	[Property] public bool StartOn { get; set; }
	[Property] public bool AutoReturn { get; set; }
	[Property] public bool Rope { get; set; }
	public float Speed { get; private set; }

	private float _angle;
	private float _velocity;
	private float _dampSpeed;
	private float _lastThink;
	private float _nextThink = float.PositiveInfinity;
	private enum Think { None, Start, Swing, Stop }
	private Think _think;

	protected override void OnStart()
	{
		Place();
		if ( !StartOn || Distance == 0f ) return;
		_think = Think.Start;
		_nextThink = Time.Now + 0.1f;
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || Distance == 0f ) return;
		if ( Speed != 0f )
		{
			if ( AutoReturn )
			{
				_velocity = MaxSpeed;
				_think = Think.Stop;
				_nextThink = Time.Now + _angle / MaxSpeed;
			}
			else
				Stop( false );
		}
		else
		{
			_lastThink = Time.Now;
			_nextThink = Time.Now + 0.1f;
			_think = Think.Swing;
			_dampSpeed = MaxSpeed;
		}
	}

	public override void TurnOn( GameObject activator = null ) => Trigger( activator );
	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	protected override void OnFixedUpdate()
	{
		if ( !CanTrigger ) return;
		_angle += _velocity * Time.Delta;
		Place();
		if ( Time.Now < _nextThink ) return;
		if ( _think == Think.Start ) { Trigger(); return; }
		if ( _think == Think.Stop ) { Stop( true ); return; }
		if ( _think != Think.Swing ) return;
		var dt = Time.Now - _lastThink;
		_lastThink = Time.Now;
		var accel = MaxSpeed * MaxSpeed / (2f * MathF.Abs( Distance ));
		Speed += (_angle > Distance * 0.5f ? -accel : accel) * dt;
		Speed = Math.Clamp( Speed, -MaxSpeed, MaxSpeed );
		_velocity = Speed;
		_nextThink = Time.Now + 0.1f;
		if ( Damping == 0f ) return;
		_dampSpeed -= Damping * _dampSpeed * dt;
		if ( _dampSpeed < 30f )
		{
			_angle = Distance * 0.5f;
			Stop( false );
		}
		else
			Speed = Math.Clamp( Speed, -_dampSpeed, _dampSpeed );
	}

	private void Place() => WorldRotation = Rotation.From( StartAngles + Axis * _angle );

	private void Stop( bool reset )
	{
		if ( reset ) _angle = 0f;
		Speed = 0f;
		_velocity = 0f;
		_think = Think.None;
		_nextThink = float.PositiveInfinity;
		Place();
	}

	public void Blocked( GameObject other ) => _lastThink = Time.Now;
	void ICollisionListener.OnCollisionStart( Collision collision ) => Touch( collision.Other.GameObject );
	void ICollisionListener.OnCollisionUpdate( Collision collision ) => Touch( collision.Other.GameObject );

	private void Touch( GameObject other )
	{
		if ( !CanTrigger || Rope || Damage <= 0f || !other.IsValid() ) return;
		var amount = MathF.Abs( Damage * Speed * 0.01f );
		BrushInteraction.Damage( GameObject, other, amount );
		BrushInteraction.SetVelocity( other, (other.WorldPosition - WorldPosition).Normal * amount );
	}
}
