using System;
using Sandbox;

namespace GoldSrc.Maps;

[Title( "GoldSrc Momentary Door" )]
[Category( "GoldSrc" )]
public sealed class MomentaryDoor : MapEntity
{
	[Property] public Vector3 SlideOffset { get; set; }
	[Property] public bool StartOpen { get; set; }
	[Property] public SoundEvent MoveSound { get; set; }
	private Vector3 _bottom;
	private Vector3 _top;
	private Vector3 _target;
	private Vector3 _velocity;
	private float _arriveAt = float.PositiveInfinity;
	private SoundHandle _sound;

	protected override void OnStart()
	{
		_bottom = WorldPosition + (StartOpen ? SlideOffset : Vector3.Zero);
		_top = WorldPosition + (StartOpen ? Vector3.Zero : SlideOffset);
		WorldPosition = _bottom;
	}

	public override void Trigger( GameObject activator = null ) { }
	public override void TurnOn( GameObject activator = null ) { }
	public override void TurnOff( GameObject activator = null ) { }

	public override void SetValue( float value, GameObject activator = null )
	{
		if ( !CanTrigger || !float.IsFinite( value ) ) return;
		var target = _bottom + Math.Clamp( value, 0f, 1f ) * (_top - _bottom);
		if ( target == WorldPosition || _arriveAt < float.PositiveInfinity && target == _target ) return;
		if ( !_sound.IsValid() && MoveSound is not null ) _sound = Sound.Play( MoveSound, WorldPosition );
		_target = target;
		_velocity = (_target - WorldPosition) / 0.1f;
		_arriveAt = Time.Now + 0.1f;
	}

	protected override void OnFixedUpdate()
	{
		if ( !CanTrigger || _arriveAt == float.PositiveInfinity ) return;
		if ( Time.Now >= _arriveAt )
		{
			WorldPosition = _target;
			_velocity = Vector3.Zero;
			_arriveAt = float.PositiveInfinity;
			StopSound();
			return;
		}
		WorldPosition += _velocity * Time.Delta;
		if ( _sound.IsValid() ) _sound.Position = WorldPosition;
	}

	protected override void OnDisabled() => StopSound();
	private void StopSound()
	{
		_sound?.Stop();
		_sound = null;
	}
}
