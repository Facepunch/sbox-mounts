using System;
using System.Collections.Generic;
using Sandbox;

namespace GoldSrc.Maps;

[Title( "GoldSrc Momentary Rotating Button" )]
[Category( "GoldSrc" )]
public sealed class MomentaryRotButton : MapEntity, Component.IPressable
{
	[Property] public Angles StartAngles { get; set; }
	[Property] public Angles Axis { get; set; } = new( 0f, 1f, 0f );
	[Property] public float Distance { get; set; }
	[Property] public float Speed { get; set; } = 100f;
	[Property] public float ReturnSpeed { get; set; }
	[Property] public bool AutoReturn { get; set; }
	[Property] public bool UseEnabled { get; set; } = true;
	[Property] public SoundEvent MoveSound { get; set; }
	public float Fraction => _distance == 0f ? 0f : _angle / _distance;

	private Angles _start;
	private float _distance;
	private float _angle;
	private float _velocity;
	private int _direction;
	private bool _used;
	private bool _returning;
	private float _nextThink = float.PositiveInfinity;

	protected override void OnStart()
	{
		_distance = MathF.Abs( Distance );
		_start = StartAngles + (Distance < 0f ? Axis * Distance : default);
		_angle = Distance < 0f ? _distance : 0f;
		_direction = Distance < 0f ? 1 : -1;
		Place();
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || _distance == 0f ) return;
		var fraction = Fraction;
		foreach ( var button in LinkedButtons() ) button.StartUsing( fraction );
		var predicted = (_angle + _velocity * (_nextThink - Time.Now)) / _distance;
		UpdateTargets( predicted );
	}

	public override void TurnOn( GameObject activator = null ) => Trigger( activator );
	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	private IEnumerable<MomentaryRotButton> LinkedButtons()
	{
		if ( !Map.IsValid() )
		{
			yield return this;
			yield break;
		}
		foreach ( var entity in Map.Entities )
			if ( entity is MomentaryRotButton button && button.IsValid() && button.Target == Target )
				yield return button;
	}

	private void StartUsing( float fraction )
	{
		var play = !_used;
		if ( play ) _direction = -_direction;
		_used = true;
		_nextThink = Time.Now + 0.1f;
		if ( _direction > 0 && fraction >= 1f || _direction < 0 && fraction <= 0f )
		{
			_velocity = 0f;
			_angle = _direction > 0 ? _distance : 0f;
			Place();
			return;
		}
		if ( play && MoveSound is not null ) Sound.Play( MoveSound, WorldPosition );
		_nextThink += 0.1f;
		_velocity = _direction * Speed;
		_returning = false;
	}

	private void UpdateTargets( float value )
	{
		if ( string.IsNullOrEmpty( Target ) || !Map.IsValid() ) return;
		foreach ( var entity in Map.Entities.ToArray() )
			if ( entity.IsValid() && entity.TargetName == Target )
				entity.Use( GameObject, this, (int)UseType.Set, value );
	}

	protected override void OnFixedUpdate()
	{
		if ( !CanTrigger ) return;
		_angle += _velocity * Time.Delta;
		Place();
		if ( Time.Now < _nextThink ) return;
		if ( !_returning )
		{
			_velocity = 0f;
			_used = false;
			_returning = AutoReturn && ReturnSpeed > 0f;
			_nextThink = _returning ? Time.Now + 0.1f : float.PositiveInfinity;
			if ( _returning ) _direction = -1;
			return;
		}
		var fraction = Fraction;
		foreach ( var button in LinkedButtons() )
		{
			if ( fraction <= 0f )
			{
				button._velocity = 0f;
				button._angle = 0f;
				button._nextThink = float.PositiveInfinity;
				button._returning = false;
				button.Place();
			}
			else
			{
				button._velocity = -button.ReturnSpeed;
				button._nextThink = Time.Now + 0.1f;
			}
		}
		if ( fraction > 0f ) UpdateTargets( fraction );
	}

	private void Place() => WorldRotation = Rotation.From( _start + Axis * _angle );
	bool IPressable.CanPress( IPressable.Event e ) => CanTrigger && UseEnabled;
	bool IPressable.Press( IPressable.Event e )
	{
		if ( !UseEnabled || !CanTrigger ) return false;
		Trigger( e.Source.GameObject );
		return true;
	}
	bool IPressable.Pressing( IPressable.Event e )
	{
		if ( !UseEnabled || !CanTrigger ) return false;
		Trigger( e.Source.GameObject );
		return true;
	}
}
