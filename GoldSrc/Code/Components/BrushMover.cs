using System;
using Sandbox;

namespace GoldSrc.Maps;

[Title( "GoldSrc Brush Mover" )]
[Category( "GoldSrc" )]
[Icon( "open_with" )]
public sealed class BrushMover : MapEntity, Component.IPressable, Component.ICollisionListener, Component.IDamageable
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
	[Property] public bool Shootable { get; set; }
	[Property] public float Damage { get; set; }
	[Property] public BrushAppearance Appearance { get; set; }
	[Property] public SparkEffect Sparks { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent MoveSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent StopSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent LockedSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent UnlockedSound { get; set; }
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
	private bool _arrivalPending;
	private float _lockSoundAt;
	private float _sparkAt = float.PositiveInfinity;

	protected override void OnStart()
	{
		_position = WorldPosition;
		_bottomAngles = StartAngles + (StartOpen ? AngleOffset : default);
		_topAngles = StartAngles + (StartOpen ? default : AngleOffset);
		if ( Sparks.IsValid() && !Rotating ) _sparkAt = Time.Now + 0.5f;
		Place();
	}

	protected override void OnDisabled() => StopMovingSound();

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || !CanActivate() || IsButton && !UseEnabled ) return;
		Activate( activator );
	}

	private void Activate( GameObject activator )
	{
		_activator = activator;

		if ( _state == MoveState.Top )
		{
			if ( !IsButton && !IsMasterTriggered( activator ) ) return;
			if ( IsButton ) PlayButtonSound();
			Begin( false );
			return;
		}

		if ( IsButton ) PlayButtonSound();
		if ( !IsMasterTriggered( activator ) )
		{
			PlayLockSound( true );
			return;
		}
		PlayLockSound( false );
		_topAngles = StartAngles + (StartOpen ? default : AngleOffset);
		if ( Rotating && !IsButton && !OneWay && AngleOffset.yaw != 0f && activator.IsValid() )
		{
			var offset = activator.WorldPosition - _position;
			var facing = activator.WorldRotation.Forward;
			if ( offset.x * facing.y - offset.y * facing.x < 0f )
				_topAngles *= -1f;
		}

		Begin( true );
	}

	public override void TurnOn( GameObject activator = null ) => Trigger( activator );
	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	private bool CanActivate() => _state == MoveState.Bottom
		|| _state == MoveState.Top && Toggle && (!IsButton || Wait != -1f);

	private void Begin( bool up )
	{
		_state = up ? MoveState.GoingUp : MoveState.GoingDown;
		_sparkAt = float.PositiveInfinity;
		_arrivalPending = true;
		_returnAt = float.PositiveInfinity;
		if ( IsButton && !up && Appearance.IsValid() ) Appearance.Frame = 0;

		if ( !IsButton && MoveSound is not null && !_sound.IsValid() )
			_sound = Sound.Play( MoveSound, WorldPosition );

		if ( TravelDistance == 0f )
			Arrive( up );
	}

	private float TravelDistance => Rotating ? (_topAngles - _bottomAngles).AsVector3().Length : SlideOffset.Length;

	protected override void OnFixedUpdate()
	{
		if ( !CanTrigger ) return;
		if ( _state == MoveState.Bottom && Sparks.IsValid() && Time.Now >= _sparkAt )
		{
			Sparks.Emit();
			_sparkAt = Time.Now + 0.1f + Game.Random.Float( 0f, 1.5f );
		}

		if ( _state == MoveState.Top && Time.Now >= _returnAt )
			Begin( false );

		if ( !_arrivalPending || _state is not (MoveState.GoingUp or MoveState.GoingDown) )
			return;

		var up = _state == MoveState.GoingUp;
		var target = up ? 1f : 0f;
		var step = TravelDistance > 0f ? MathF.Abs( Speed ) * Time.Delta / TravelDistance : 1f;
		_fraction += Math.Clamp( target - _fraction, -step, step );
		Place();

		if ( _sound.IsValid() )
			_sound.Position = WorldPosition;

		if ( _fraction == target )
			Arrive( up );
	}

	internal sealed record SavedMove( int State, float Fraction, float? ReturnIn );
	internal SavedMove CaptureMove() => new( (int)_state, _fraction, float.IsFinite( _returnAt ) ? _returnAt - Time.Now : null );

	internal void RestoreMove( SavedMove saved )
	{
		_state = (MoveState)saved.State;
		_fraction = saved.Fraction;
		_returnAt = saved.ReturnIn.HasValue ? Time.Now + saved.ReturnIn.Value : float.PositiveInfinity;
		_arrivalPending = _state is MoveState.GoingUp or MoveState.GoingDown;
		Place();
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
		_arrivalPending = false;
		if ( IsButton && up && !IsMasterTriggered( _activator ) ) return;
		_state = up ? MoveState.Top : MoveState.Bottom;
		if ( IsButton && up && Appearance.IsValid() ) Appearance.Frame = 1;
		if ( IsButton && !up && Sparks.IsValid() ) _sparkAt = Time.Now + 0.5f;
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

		if ( IsButton && !up && Map.IsValid() && !string.IsNullOrEmpty( Target ) )
		{
			foreach ( var target in Map.Entities.ToArray() )
				if ( target.IsValid() && target.TargetName == Target && target is MultiSource )
					target.Use( _activator, this, (int)UseType.Toggle, 0f );
		}
	}

	private void PlayLockSound( bool locked )
	{
		if ( Time.Now < _lockSoundAt ) return;
		var sound = locked ? LockedSound : UnlockedSound;
		if ( sound is null ) return;
		Sound.Play( sound, WorldPosition );
		_lockSoundAt = Time.Now + (IsButton ? 0.5f : 3f);
	}

	public void Blocked( GameObject other )
	{
		if ( !CanTrigger || IsButton || _state is not (MoveState.GoingUp or MoveState.GoingDown) ) return;
		BrushInteraction.Damage( GameObject, other, Damage );
		if ( Wait >= 0f ) Begin( _state == MoveState.GoingDown );
		if ( string.IsNullOrEmpty( TargetName ) || !Map.IsValid() ) return;
		foreach ( var entity in Map.Entities )
			if ( entity is BrushMover door && door != this && !door.IsButton && door.TargetName == TargetName && door.Wait >= 0f )
				door.Begin( door._state == MoveState.GoingDown );
	}

	void IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( !CanTrigger || !IsButton || !Shootable || !CanActivate() || !damage.Attacker.IsValid() ) return;
		Activate( damage.Attacker );
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
		if ( !IsMasterTriggered( other ) || !IsButton && !string.IsNullOrEmpty( TargetName ) )
		{
			PlayLockSound( true );
			return;
		}

		if ( IsButton && _state == MoveState.Top )
			Fire( OnTrigger, other );
		Activate( other );
	}
}
