using System;
using Sandbox;

namespace GoldSrc.Maps;

[Title( "GoldSrc Platform" )]
[Category( "GoldSrc" )]
public class PlatformBrush : MapEntity
{
	[Property] public float Height { get; set; }
	[Property] public float Speed { get; set; } = 150f;
	[Property] public Angles AngleOffset { get; set; }
	[Property] public bool StartAtTop { get; set; }
	[Property] public bool Toggle { get; set; }
	[Property] public SoundEvent MoveSound { get; set; }
	[Property] public SoundEvent StopSound { get; set; }
	[Property] public float Volume { get; set; } = 0.85f;
	public enum PlatformState { Top, Bottom, GoingUp, GoingDown }
	public PlatformState State { get; protected set; }
	public bool Moving => State is PlatformState.GoingUp or PlatformState.GoingDown;
	protected Vector3 TopPosition;
	protected Vector3 BottomPosition;
	protected Angles CurrentAngles;
	protected float ReturnAt = float.PositiveInfinity;
	protected bool UseAvailable;
	private Vector3 _moveStart;
	private Vector3 _moveEnd;
	private Angles _angleStart;
	private Angles _angleEnd;
	private float _moveStarted;
	private float _moveTime;
	private SoundHandle _sound;

	protected override void OnStart()
	{
		TopPosition = WorldPosition;
		BottomPosition = TopPosition - Vector3.Up * Height;
		State = StartAtTop ? PlatformState.Top : PlatformState.Bottom;
		UseAvailable = !string.IsNullOrEmpty( TargetName );
		WorldPosition = StartAtTop ? TopPosition : BottomPosition;
		CurrentAngles = StartAtTop ? AngleOffset : default;
		WorldRotation = Rotation.From( CurrentAngles );
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || !UseAvailable || Moving ) return;
		if ( Toggle )
			Begin( State == PlatformState.Bottom );
		else
		{
			UseAvailable = false;
			if ( State == PlatformState.Top ) Begin( false );
		}
	}

	public override void TurnOn( GameObject activator = null )
	{
		if ( !Toggle || State != PlatformState.Bottom ) Trigger( activator );
	}

	public override void TurnOff( GameObject activator = null )
	{
		if ( !Toggle || State != PlatformState.Top ) Trigger( activator );
	}

	public void TouchPlatform( GameObject other )
	{
		if ( !CanTrigger || Toggle || !BrushInteraction.IsPlayer( other ) ) return;
		if ( State == PlatformState.Bottom ) Begin( true );
		else if ( State == PlatformState.Top && ReturnAt < float.PositiveInfinity ) ReturnAt = Time.Now + 1f;
	}

	protected virtual bool RotationOnly => false;

	protected virtual void Begin( bool up )
	{
		ReturnAt = float.PositiveInfinity;
		_moveStart = WorldPosition;
		_moveEnd = up ? TopPosition : BottomPosition;
		_angleStart = CurrentAngles;
		_angleEnd = up ? AngleOffset : default;
		_moveStarted = Time.Now;
		_moveTime = (RotationOnly ? (_angleEnd - _angleStart).AsVector3().Length : (_moveEnd - _moveStart).Length) / Speed;
		if ( !RotationOnly && _moveTime < 0.1f && _angleStart != _angleEnd ) _moveTime = 1f;
		State = up ? PlatformState.GoingUp : PlatformState.GoingDown;
		if ( !RotationOnly && MoveSound is not null )
		{
			StopMovingSound();
			_sound = Sound.Play( MoveSound, WorldPosition );
			_sound.Volume = Volume;
		}
		OnBeginMove( up, _moveTime );
		if ( _moveTime <= 0f ) Arrive( up );
	}

	protected virtual void OnBeginMove( bool up, float duration ) { }
	protected virtual void OnMove( float fraction ) { }

	protected override void OnFixedUpdate()
	{
		if ( !CanTrigger ) return;
		if ( State == PlatformState.Top && Time.Now >= ReturnAt ) Begin( false );
		if ( !Moving ) return;
		var fraction = _moveTime > 0f ? Math.Clamp( (Time.Now - _moveStarted) / _moveTime, 0f, 1f ) : 1f;
		WorldPosition = Vector3.Lerp( _moveStart, _moveEnd, fraction );
		CurrentAngles = _angleStart + (_angleEnd - _angleStart) * fraction;
		WorldRotation = Rotation.From( CurrentAngles );
		OnMove( fraction );
		if ( _sound.IsValid() ) _sound.Position = WorldPosition;
		if ( fraction == 1f ) Arrive( State == PlatformState.GoingUp );
	}

	protected virtual void Arrive( bool up )
	{
		State = up ? PlatformState.Top : PlatformState.Bottom;
		WorldPosition = up ? TopPosition : BottomPosition;
		CurrentAngles = up ? AngleOffset : default;
		WorldRotation = Rotation.From( CurrentAngles );
		StopMovingSound();
		if ( StopSound is not null )
		{
			var sound = Sound.Play( StopSound, WorldPosition );
			sound.Volume = Volume;
		}
		ReturnAt = up && !Toggle ? Time.Now + 3f : float.PositiveInfinity;
	}

	public void Blocked( GameObject other )
	{
		if ( !CanTrigger || !Moving ) return;
		BrushInteraction.Damage( GameObject, other, 1f );
		Begin( State == PlatformState.GoingDown );
	}

	protected override void OnDisabled() => StopMovingSound();
	private void StopMovingSound()
	{
		_sound?.Stop();
		_sound = null;
	}
}

[Title( "GoldSrc Platform Trigger" )]
[Category( "GoldSrc" )]
public sealed class PlatformTrigger : Component, Component.ITriggerListener
{
	[Property] public PlatformBrush Platform { get; set; }
	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( Platform.IsValid() && other.IsValid() ) Platform.TouchPlatform( other.GameObject );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Platform.IsValid() )
		{
			GameObject.Destroy();
			return;
		}
		var collider = GetComponent<Collider>();
		if ( !collider.IsValid() ) return;
		foreach ( var other in collider.Touching )
			if ( other.IsValid() ) Platform.TouchPlatform( other.GameObject );
	}
}
