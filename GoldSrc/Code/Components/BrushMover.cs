using System;
using Sandbox;

namespace GoldSrc;

[Title( "GoldSrc Brush Mover" )]
[Category( "GoldSrc" )]
[Icon( "open_with" )]
public sealed class BrushMover : MapEntity, Component.IPressable, Component.ICollisionListener
{
	[Property] public bool IsButton { get; set; }
	[Property] public bool Rotating { get; set; }
	[Property] public Vector3 SlideOffset { get; set; }
	[Property] public Angles StartAngles { get; set; }
	[Property] public Angles AngleOffset { get; set; }
	[Property] public float Speed { get; set; } = 100f;
	[Property] public float Wait { get; set; } = 4f;
	[Property] public bool StartOpen { get; set; }
	[Property] public bool Toggle { get; set; }
	[Property] public bool UseEnabled { get; set; }
	[Property] public bool TouchEnabled { get; set; }
	[Property] public bool OneWay { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent MoveSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent StopSound { get; set; }
	[Property, Group( "Events" )] public Doo OnClose { get; set; }

	private enum MoveState { Bottom, GoingUp, Top, GoingDown }
	private MoveState _state;
	private Vector3 _position;
	private float _fraction;
	private Angles _bottomAngles;
	private Angles _topAngles;
	private float _returnAt = float.PositiveInfinity;
	private GameObject _activator;
	private SoundHandle _sound;

	protected override void OnStart()
	{
		_position = WorldPosition;
		_bottomAngles = StartAngles + (StartOpen ? AngleOffset : default);
		_topAngles = StartAngles + (StartOpen ? default : AngleOffset);
		Place();
	}

	protected override void OnDisabled() => StopMovingSound();

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || !CanActivate() ) return;
		_activator = activator;

		if ( _state == MoveState.Top )
		{
			if ( IsButton ) PlayButtonSound();
			Begin( false );
			return;
		}

		_topAngles = StartAngles + (StartOpen ? default : AngleOffset);
		if ( Rotating && !IsButton && !OneWay && AngleOffset.yaw != 0f && activator.IsValid() )
		{
			var offset = activator.WorldPosition - _position;
			var facing = activator.WorldRotation.Forward;
			if ( offset.x * facing.y - offset.y * facing.x < 0f )
				_topAngles *= -1f;
		}

		if ( IsButton ) PlayButtonSound();
		Begin( true );
	}

	public override void TurnOn( GameObject activator = null ) => Trigger( activator );
	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	private bool CanActivate() => _state == MoveState.Bottom
		|| _state == MoveState.Top && Toggle && (!IsButton || Wait != -1f);

	private void Begin( bool up )
	{
		_state = up ? MoveState.GoingUp : MoveState.GoingDown;
		_returnAt = float.PositiveInfinity;

		if ( !IsButton && MoveSound is not null && !_sound.IsValid() )
			_sound = Sound.Play( MoveSound, WorldPosition );

		if ( TravelDistance == 0f )
			Arrive( up );
	}

	private float TravelDistance => Rotating ? (_topAngles - _bottomAngles).AsVector3().Length : SlideOffset.Length;

	protected override void OnFixedUpdate()
	{
		if ( !CanTrigger ) return;

		if ( _state == MoveState.Top && Time.Now >= _returnAt )
			Begin( false );

		if ( _state is not (MoveState.GoingUp or MoveState.GoingDown) )
			return;

		var up = _state == MoveState.GoingUp;
		var target = up ? 1f : 0f;
		var step = MathF.Abs( Speed ) * Time.Delta / TravelDistance;
		_fraction += Math.Clamp( target - _fraction, -step, step );
		Place();

		if ( _sound.IsValid() )
			_sound.Position = WorldPosition;

		if ( _fraction == target )
			Arrive( up );
	}

	private void Place()
	{
		if ( Rotating )
			WorldRotation = Rotation.From( _bottomAngles + (_topAngles - _bottomAngles) * _fraction );
		else
			WorldPosition = _position + SlideOffset * (StartOpen ? 1f - _fraction : _fraction);
	}

	private void Arrive( bool up )
	{
		_state = up ? MoveState.Top : MoveState.Bottom;
		StopMovingSound();
		if ( !IsButton && StopSound is not null )
			Sound.Play( StopSound, WorldPosition );

		if ( up && !Toggle && Wait != -1f )
			_returnAt = Time.Now + MathF.Max( Wait, 0f );

		if ( !IsButton && StartOpen && up )
			Fire( OnClose, _activator );

		if ( !IsButton || up || Toggle )
			Fire( OnTrigger, _activator );

		if ( !IsButton && !StartOpen && !up )
			Fire( OnClose, _activator );
	}

	private void PlayButtonSound()
	{
		if ( MoveSound is not null )
			Sound.Play( MoveSound, WorldPosition );
	}

	private void StopMovingSound()
	{
		_sound?.Stop();
		_sound = null;
	}

	bool IPressable.CanPress( IPressable.Event e ) => UseEnabled && CanTrigger && CanActivate();

	bool IPressable.Press( IPressable.Event e )
	{
		if ( !UseEnabled || !CanTrigger || !CanActivate() ) return false;
		Trigger( e.Source.GameObject );
		return true;
	}

	void ICollisionListener.OnCollisionStart( Collision collision ) => Touch( collision.Other.GameObject );
	void ICollisionListener.OnCollisionUpdate( Collision collision ) => Touch( collision.Other.GameObject );

	private void Touch( GameObject other )
	{
		if ( !TouchEnabled || !other.IsValid() || !CanActivate() ) return;
		if ( !other.Tags.Has( "player" ) && !other.GetComponentInParent<PlayerController>().IsValid() ) return;

		if ( IsButton && _state == MoveState.Top )
			Fire( OnTrigger, other );
		Trigger( other );
	}
}
