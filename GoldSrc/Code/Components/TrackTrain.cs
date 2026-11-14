using System;
using Sandbox;

namespace GoldSrc;

/// <summary>
/// func_tracktrain and func_train. Both run a chain of <see cref="PathNode"/>s; a tracktrain
/// orients itself using a forward wheelbase sample; a func_train just
/// slides between path_corners without turning.
///
/// Tracktrains pivot at their origin brush. Ordinary trains place their brush centre on each corner.
/// </summary>
[Title( "GoldSrc Track Train" )]
[Icon( "tram" )]
public sealed class TrackTrain : MapEntity
{
	/// <summary>The node named by the entity's "target" key - where the train starts.</summary>
	[Property] public PathNode Path { get; set; }

	/// <summary>The "speed" key: the speed the train runs at when it is switched on.</summary>
	[Property] public float MaxSpeed { get; set; } = 100f;

	/// <summary>The "wheels" key: how far ahead the train samples its track.</summary>
	[Property] public float WheelBase { get; set; } = 50f;

	/// <summary>The "height" key: how far above the track the body sits.</summary>
	[Property] public float Height { get; set; }

	/// <summary>SF_TRACKTRAIN_NOPITCH - the train yaws to follow the track but stays level.</summary>
	[Property] public bool NoPitch { get; set; }

	/// <summary>A func_train slides along its path without ever turning.</summary>
	[Property] public bool NoRotation { get; set; }
	[Property] public float StartSpeed { get; set; }
	[Property] public bool NoControl { get; set; }
	[Property] public bool WaitForTrigger { get; set; }
	[Property] public float Bank { get; set; }

	/// <summary>The looping "sounds" WAV, played while the train is moving.</summary>
	[Property, Group( "Sounds" )] public SoundEvent MoveSound { get; set; }

	/// <summary>The train's current speed. Zero means stopped.</summary>
	public float Speed { get; private set; }

	private PathNode _node;
	private PathNode _cornerTarget;
	private Vector3 _trackPosition;
	private float _waitUntilTime;
	private float _startTime;
	private float _roll;
	private float _resumeSpeed;
	private bool _started;
	private SoundHandle _moveSound;

	protected override void OnStart()
	{
		if ( !Path.IsValid() )
			return;

		_node = Path;
		_resumeSpeed = MaxSpeed;
		_trackPosition = Path.WorldPosition;
		_cornerTarget = NoRotation ? Path.Next : null;
		_startTime = Time.Now + 0.1f;
		Place();
	}

	protected override void OnDisabled() => StopSound();

	/// <summary>CBaseEntity::Use with USE_TOGGLE: running trains stop, stopped trains resume.</summary>
	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || !_node.IsValid() ) return;
		_started = true;
		if ( NoRotation )
		{
			if ( WaitForTrigger )
			{
				WaitForTrigger = false;
				_waitUntilTime = 0f;
				SetSpeed( _node.Speed != 0f ? _node.Speed : _resumeSpeed );
			}
			else
			{
				WaitForTrigger = true;
				SetSpeed( 0f );
			}
			return;
		}
		if ( Speed != 0f ) SetSpeed( 0f );
		else
		{
			WaitForTrigger = false;
			_waitUntilTime = 0f;
			SetSpeed( MaxSpeed );
		}
	}

	// CBaseToggle::ShouldToggle - an explicit on/off that matches the current state is ignored.
	public override void TurnOn( GameObject activator = null )
	{
		if ( NoRotation ) { Trigger( activator ); return; }
		if ( Speed == 0f )
			Trigger( activator );
	}

	public override void TurnOff( GameObject activator = null )
	{
		if ( NoRotation ) { Trigger( activator ); return; }
		if ( Speed != 0f )
			Trigger( activator );
	}

	/// <summary>
	/// Change speed without going through a use type - this is what a path_track's "speed" key does.
	/// </summary>
	public void SetSpeed( float speed )
	{
		if ( !CanTrigger ) return;
		Speed = speed;
		if ( NoRotation && speed != 0f ) _resumeSpeed = speed;

		if ( Speed == 0f )
		{
			StopSound();
			return;
		}

		StartSound();
	}

	private void StartSound()
	{
		if ( MoveSound is null || _moveSound.IsValid() || Speed == 0f )
			return;

		_moveSound = Sound.Play( MoveSound, WorldPosition );
		_moveSound.Pitch = PitchForSpeed( Speed );
	}

	protected override void OnFixedUpdate()
	{
		if ( !CanTrigger || !_node.IsValid() ) return;
		if ( !_started && Time.Now >= _startTime )
		{
			_started = true;
			SetSpeed( NoRotation && StartSpeed != 0f && _node.Speed != 0f ? _node.Speed : StartSpeed );
		}

		if ( _moveSound.IsValid() )
		{
			_moveSound.Position = WorldPosition;
			_moveSound.Pitch = PitchForSpeed( Speed );
		}

		if ( Speed == 0f )
			return;

		if ( Time.Now < _waitUntilTime )
			return;

		StartSound();
		Advance( Time.Delta );
		Place();
	}

	/// <summary>
	/// CFuncTrackTrain::Next pitches the running sound with the train's speed, ramping from
	/// TRAIN_STARTPITCH (60) at a standstill to TRAIN_MAXPITCH (200) at TRAIN_MAXSPEED (1000).
	/// GoldSrc pitches are percentages, so 100 is unmodified.
	/// </summary>
	private static float PitchForSpeed( float speed )
		=> (60f + MathF.Abs( speed ) * (200f - 60f) / 1000f) / 100f;

	/// <summary>
	/// Advance by <paramref name="time"/> seconds, firing every node passed on the way. The
	/// loop is bounded because a path that loops back on itself with tiny segments could otherwise
	/// consume the whole budget in one frame.
	/// </summary>
	private void Advance( float time )
	{
		for ( var guard = 0; time > 0f && Speed != 0f && guard < 1024; guard++ )
		{
			var forward = NoRotation || Speed > 0f;
			var target = NoRotation ? _cornerTarget : forward ? _node.NextEnabled( true ) : _node;
			if ( !target.IsValid() )
			{
				SetSpeed( 0f );
				if ( !NoRotation ) _node.DeadEnd( GameObject );
				return;
			}

			var toTarget = target.WorldPosition - _trackPosition;
			var length = toTarget.Length;
			if ( NoRotation && target.Teleport )
				length = 0f;

			var distance = MathF.Abs( Speed ) * time;
			if ( length > distance )
			{
				_trackPosition += toTarget / length * distance;
				return;
			}

			_trackPosition = target.WorldPosition;
			time -= length / MathF.Abs( Speed );
			Place();
			if ( NoRotation )
			{
				_node = target;
				_cornerTarget = target.Next;
				target.Pass( GameObject );
				if ( !CanTrigger || !target.IsValid() || Speed == 0f ) return;
				if ( target.Speed != 0f ) SetSpeed( target.Speed );
				if ( target.WaitForTrigger || WaitForTrigger || target.Wait < 0f )
				{
					SetSpeed( 0f );
					WaitForTrigger = true;
					return;
				}
				if ( target.Wait > 0f )
				{
					_waitUntilTime = Time.Now + target.Wait;
					StopSound();
					return;
				}
			}
			else
			{
				if ( !forward )
				{
					var previous = _node.NextEnabled( false );
					if ( !previous.IsValid() )
					{
						SetSpeed( 0f );
						_node.DeadEnd( GameObject );
						return;
					}
					_node = previous;
				}
				else
					_node = target;

				target.Pass( GameObject );
				if ( !CanTrigger || !target.IsValid() || Speed == 0f ) return;
				if ( target.DisablesControls ) NoControl = true;
				if ( NoControl && target.Speed != 0f )
					SetSpeed( target.Speed );
			}
		}

		if ( time > 0f && Speed != 0f )
		{
			Log.Warning( $"GoldSrc train '{TargetName}' exceeded its path traversal limit; check for zero-length path cycles." );
			SetSpeed( 0f );
		}
	}

	private void Place()
	{
		WorldPosition = _trackPosition + Vector3.Up * Height;

		if ( NoRotation )
			return;

		var direction = Sample( WheelBase > 0f ? WheelBase : 100f ) - _trackPosition;
		if ( NoPitch )
			direction = direction.WithZ( 0f );
		if ( direction.IsNearlyZero() )
			return;

		// Half-Life's tracktrain models face local -X, including when travelling backwards.
		var angles = Rotation.LookAt( -direction.Normal, Vector3.Up ).Angles();
		if ( Bank != 0f )
		{
			var yawSpeed = MathF.IEEERemainder( angles.yaw - WorldRotation.Angles().yaw, 360f ) / MathF.Max( Time.Delta, 0.001f );
			var targetRoll = yawSpeed < -5f ? -Bank : yawSpeed > 5f ? Bank : 0f;
			var rate = MathF.Abs( Bank ) * (targetRoll == 0f ? 4f : 2f);
			_roll += Math.Clamp( targetRoll - _roll, -rate, rate ) * Time.Delta * (targetRoll == 0f ? 4f : 1f);
		}
		angles.roll = _roll;
		WorldRotation = Rotation.From( angles );
	}

	private Vector3 Sample( float distance )
	{
		var position = _trackPosition;
		var node = _node;

		for ( var guard = 0; distance > 0f && node.IsValid() && guard < 1024; guard++ )
		{
			var next = node.NextEnabled( true, testDisabled: false );
			if ( !next.IsValid() )
			{
				var previous = node.NextEnabled( false, testDisabled: false );
				if ( previous.IsValid() )
				{
					var direction = node.WorldPosition - previous.WorldPosition;
					if ( !direction.IsNearlyZero() )
						return position + direction.Normal * distance;
				}
				return position;
			}
			var offset = next.WorldPosition - position;
			var length = offset.Length;
			if ( length > distance )
				return position + offset / length * distance;
			position = next.WorldPosition;
			distance -= length;
			node = next;
		}

		return position;
	}

	private void StopSound()
	{
		_moveSound?.Stop();
		_moveSound = null;
	}
}
