using System;
using Sandbox;

namespace GoldSrc.Maps;

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
	[Property] public string GlobalName { get; set; }

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
	[Property] public Vector3 CornerOffset { get; set; }
	[Property] public float StartSpeed { get; set; }
	[Property] public bool NoControl { get; set; }
	[Property] public bool ForwardOnly { get; set; }
	[Property] public bool WaitForTrigger { get; set; }
	[Property] public float Bank { get; set; }
	[Property] public float Damage { get; set; }
	[Property] public float Volume { get; set; } = 1f;
	[Property] public SoundEvent StartSoundEvent { get; set; }
	[Property] public SoundEvent StopSoundEvent { get; set; }
	[Property] public Vector3 PlacedOrigin { get; set; }
	[Property] public BBox ControlBounds { get; set; }
	public PathNode CurrentPath => _node;

	public bool OnControls( GameObject player ) => !NoControl && player.IsValid()
		&& ControlBounds.Contains( WorldTransform.PointToLocal( player.WorldPosition ) );

	/// <summary>The looping "sounds" WAV, played while the train is moving.</summary>
	[Property, Group( "Sounds" )] public SoundEvent MoveSound { get; set; }

	/// <summary>The train's current speed. Zero means stopped.</summary>
	public float Speed { get; private set; }

	private PathNode _node;
	private PathNode _cornerTarget;
	private Vector3 _trackPosition;
	private float _waitUntilTime;
	private float _startTime;
	private float _resumeSpeed;
	private bool _started;
	private SoundHandle _moveSound;
	private float _damageAt;
	private bool _suspended;
	private Vector3 _trackVelocity;
	private Angles _angularVelocity;
	private Angles _trackAngles;
	private float _deadEndAt = float.PositiveInfinity;
	private float _oldSpeed;
	private bool _cornerMoving;

	protected override void OnStart()
	{
		if ( _started || !Path.IsValid() )
			return;

		_node = Path;
		_resumeSpeed = MaxSpeed;
		_trackPosition = Path.WorldPosition;
		_cornerTarget = NoRotation ? Path.Next : null;
		if ( NoRotation ) Target = Path.Target;
		_startTime = Time.Now + 0.1f;
		Place();
		_trackAngles = WorldRotation.Angles();
	}

	protected override void OnDisabled() => StopSound();

	internal sealed record SavedTrain( int Node, int Corner, string Target, bool Started, bool Moving, bool Waiting,
		float? Wait, float Speed, float ResumeSpeed, Vector3 Velocity, Angles AngularVelocity, float? DeadEnd, float OldSpeed );

	internal SavedTrain CaptureTrain() => !_node.IsValid() ? null : new( _node.EntityIndex,
		_cornerTarget.IsValid() ? _cornerTarget.EntityIndex : -1, Target, _started, _cornerMoving, WaitForTrigger,
		float.IsFinite( _waitUntilTime ) ? MathF.Max( 0, _waitUntilTime - Time.Now ) : null, Speed, _resumeSpeed, _trackVelocity, _angularVelocity,
		float.IsFinite( _deadEndAt ) ? _deadEndAt - Time.Now : null, _oldSpeed );

	internal void RestoreTrain( Transform transform, SavedTrain state )
	{
		PathNode Node( int index ) => Map.Entities.OfType<PathNode>().FirstOrDefault( n => n.EntityIndex == index );
		_node = Node( state.Node );
		_cornerTarget = Node( state.Corner );
		_started = state.Started;
		_cornerMoving = state.Moving;
		WaitForTrigger = state.Waiting;
		Target = state.Target;
		_waitUntilTime = state.Wait.HasValue ? Time.Now + state.Wait.Value : float.PositiveInfinity;
		Speed = state.Speed;
		_resumeSpeed = state.ResumeSpeed;
		_trackVelocity = state.Velocity;
		_angularVelocity = state.AngularVelocity;
		_oldSpeed = state.OldSpeed;
		_deadEndAt = state.DeadEnd.HasValue ? Time.Now + state.DeadEnd.Value : float.PositiveInfinity;
		WorldTransform = transform;
		_trackPosition = transform.Position - CornerOffset - Vector3.Up * Height;
		_trackAngles = transform.Rotation.Angles();
		Transform.ClearInterpolation();
	}

	internal void ResumeTrack( Vector3 position, Rotation rotation, float speed )
	{
		_started = true;
		_trackPosition = position - Vector3.Up * Height;
		_trackAngles = rotation.Angles();
		WorldPosition = position;
		WorldRotation = rotation;
		Transform.ClearInterpolation();
		// CFuncTrackTrain::NearestPath rebinds after a global restore by position.
		// Track names and the train's default starting node can differ across BSPs.
		_node = Map.Entities.OfType<PathNode>().Where( n => n.IsValid() && n.IsTrack )
			.MinBy( n => n.WorldPosition.DistanceSquared( position ) );
		if ( _node.IsValid() && _node.WorldPosition.Distance( position ) >= 1024f ) _node = null;
		SetSpeed( _node.IsValid() ? speed : 0f );
	}

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
				if ( _cornerTarget.IsValid() ) Target = _cornerTarget.TargetName;
				_cornerMoving = false;
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

	public override void SetValue( float value, GameObject activator = null )
	{
		if ( NoRotation ) { Trigger( activator ); return; }
		if ( !CanTrigger || !float.IsFinite( value ) || (int)MaxSpeed == 0 ) return;
		_started = true;
		var fraction = (int)(Speed * 4f) / (int)MaxSpeed * 0.25f + value * 0.25f;
		SetSpeed( MaxSpeed * Math.Clamp( fraction, ForwardOnly ? 0f : -1f, 1f ) );
	}

	public override void ChangeTarget( string target )
	{
		Target = target;
	}

	public void FirePathOutput( Doo output ) => Fire( output, GameObject );

	public void SuspendTrack()
	{
		_suspended = true;
		_node = null;
		// The lift takes over movement, including any pending arrival on the old track.
		_deadEndAt = float.PositiveInfinity;
		_trackVelocity = Vector3.Zero;
		_angularVelocity = default;
	}

	public void SetTrack( PathNode path )
	{
		_node = path.IsValid() ? path.Nearest( WorldPosition ) : null;
		Path = _node;
		_suspended = false;
		_trackPosition = WorldPosition - Vector3.Up * Height;
		_trackAngles = WorldRotation.Angles();
	}

	public void Blocked( GameObject other )
	{
		if ( !CanTrigger || !other.IsValid() ) return;
		if ( NoRotation )
		{
			if ( Time.Now < _damageAt ) return;
			_damageAt = Time.Now + 0.5f;
		}
		else if ( BrushInteraction.Ground( other ) == GameObject )
		{
			var velocity = BrushInteraction.Velocity( other );
			if ( velocity.z == 0f ) BrushInteraction.SetVelocity( other, velocity.WithZ( MathF.Min( MathF.Abs( Speed ), 50f ) ) );
			return;
		}
		else
			BrushInteraction.SetVelocity( other, (other.WorldPosition - WorldPosition).Normal * Damage );
		if ( Damage > 0f ) BrushInteraction.Damage( GameObject, other, Damage );
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
			_trackVelocity = Vector3.Zero;
			_angularVelocity = default;
			_deadEndAt = float.PositiveInfinity;
			StopSound();
			return;
		}

		StartSound();
		if ( !NoRotation && _node.IsValid() && !_suspended ) UpdateTrack();
	}

	private void StartSound()
	{
		if ( MoveSound is null || _moveSound.IsValid() || Speed == 0f )
			return;

		_moveSound = Sound.Play( MoveSound, WorldPosition );
		_moveSound.Pitch = NoRotation ? 1f : PitchForSpeed( Speed );
		_moveSound.Volume = Volume;
		if ( StartSoundEvent is not null )
		{
			var sound = Sound.Play( StartSoundEvent, WorldPosition );
			sound.Volume = Volume;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( !CanTrigger || _suspended || !_node.IsValid() ) return;
		if ( !NoRotation && _started )
		{
			var delta = MathF.Min( Time.Delta, MathF.Max( _deadEndAt - Time.Now + Time.Delta, 0f ) );
			WorldPosition += _trackVelocity * delta;
			_trackAngles += _angularVelocity * delta;
			WorldRotation = Rotation.From( _trackAngles );
			_trackPosition = WorldPosition - Vector3.Up * Height;
			if ( Time.Now >= _deadEndAt )
			{
				_trackVelocity = Vector3.Zero;
				_deadEndAt = float.PositiveInfinity;
				FireDeadEnd();
			}
		}
		if ( !_started && Time.Now >= _startTime )
		{
			_started = true;
			SetSpeed( NoRotation && StartSpeed != 0f && _node.Speed != 0f ? _node.Speed : StartSpeed );
			if ( !NoRotation ) return;
		}

		if ( _moveSound.IsValid() )
		{
			_moveSound.Position = WorldPosition;
			_moveSound.Pitch = NoRotation ? 1f : PitchForSpeed( Speed );
		}

		if ( Speed == 0f )
			return;

		if ( Time.Now < _waitUntilTime )
			return;

		StartSound();
		if ( !NoRotation )
		{
			UpdateTrack();
			return;
		}
		Advance( Time.Delta );
		Place();
	}

	private static float AngleDifference( float target, float current ) => MathF.IEEERemainder( target - current, 360f );

	private void UpdateTrack()
	{
		if ( Speed == 0f || !_node.IsValid() || _suspended ) return;
		var position = WorldPosition - Vector3.Up * Height;
		var nextPosition = position;
		var next = _node.LookAhead( ref nextPosition, Speed * 0.1f, true );
		_trackVelocity = (nextPosition - position) * 10f;
		var front = position;
		_node.LookAhead( ref front, WheelBase > 0f ? WheelBase : 100f, false );
		var direction = front - position;
		if ( next.IsValid() && (direction.x != 0f || direction.y != 0f) )
		{
			var angles = Rotation.LookAt( -direction.Normal, Vector3.Up ).Angles();
			_angularVelocity.pitch = NoPitch ? 0f : AngleDifference( angles.pitch, _trackAngles.pitch ) * 10f;
			_angularVelocity.yaw = AngleDifference( angles.yaw, _trackAngles.yaw ) * 10f;
			if ( Bank != 0f )
			{
				var bank = _angularVelocity.yaw < -5f ? -Bank : _angularVelocity.yaw > 5f ? Bank : 0f;
				var difference = AngleDifference( bank, _trackAngles.roll );
				var limit = MathF.Abs( Bank ) * (bank == 0f ? 4f : 2f);
				_angularVelocity.roll = Math.Clamp( difference, -limit, limit ) * (bank == 0f ? 4f : 1f);
			}
		}
		else
			_angularVelocity = default;

		if ( next.IsValid() )
		{
			if ( next == _node ) return;
			var fire = Speed >= 0f ? next : _node;
			_node = next;
			fire.Pass( GameObject );
			if ( !CanTrigger || !fire.IsValid() ) return;
			if ( fire.DisablesControls ) NoControl = true;
			if ( NoControl && fire.Speed != 0f ) Speed = fire.Speed;
			return;
		}
		StopSound();
		_angularVelocity = default;
		_oldSpeed = Speed;
		Speed = 0f;
		var remaining = nextPosition - position;
		var distance = remaining.Length;
		if ( distance > 0f )
		{
			_trackVelocity = remaining * (_oldSpeed / distance);
			_deadEndAt = Time.Now + distance / _oldSpeed;
		}
		else
		{
			_trackVelocity = Vector3.Zero;
			FireDeadEnd();
		}
	}

	private void FireDeadEnd()
	{
		var node = _node;
		for ( var count = 0; count < 10000 && node.IsValid(); count++ )
		{
			var next = node.NextEnabled( _oldSpeed >= 0f );
			if ( !next.IsValid() ) { node.DeadEnd( GameObject ); return; }
			node = next;
		}
	}

	private static float PitchForSpeed( float speed )
		=> (60f + MathF.Abs( speed ) * (200f - 60f) / 1000f) / 100f;

	private void Advance( float time )
	{
		for ( var guard = 0; time > 0f && Speed != 0f && guard < 1024; guard++ )
		{
			if ( !_cornerMoving )
			{
				_cornerTarget = Map.IsValid() ? Map.Find( Target ) as PathNode : _node.Next;
				if ( _cornerTarget.IsValid() ) Target = _cornerTarget.Target;
				if ( _node.Speed != 0f )
				{
					Speed = _node.Speed;
					_resumeSpeed = Speed;
				}
				_cornerMoving = true;
			}
			var target = _cornerTarget;
			if ( !target.IsValid() )
			{
				SetSpeed( 0f );
				return;
			}

			var toTarget = target.WorldPosition - _trackPosition;
			var length = toTarget.Length;
			if ( target.Teleport )
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
			_node = target;
			_cornerMoving = false;
			target.Pass( GameObject );
			if ( !CanTrigger || !target.IsValid() || Speed == 0f ) return;
			if ( target.WaitForTrigger || WaitForTrigger )
			{
				SetSpeed( 0f );
				WaitForTrigger = true;
				return;
			}
			if ( target.Wait != 0f )
			{
				_waitUntilTime = target.Wait > 0f ? Time.Now + target.Wait : float.PositiveInfinity;
				StopSound();
				return;
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
		WorldPosition = _trackPosition + CornerOffset + Vector3.Up * Height;

		if ( NoRotation )
			return;

		var front = _trackPosition;
		_node.LookAhead( ref front, WheelBase, false );
		var direction = front - _trackPosition;
		if ( NoPitch )
			direction = direction.WithZ( 0f );
		var angles = direction.IsNearlyZero() ? new Angles( 0f, 180f, 0f ) : Rotation.LookAt( -direction.Normal, Vector3.Up ).Angles();
		WorldRotation = Rotation.From( angles );
	}

	private void StopSound()
	{
		if ( _moveSound.IsValid() && StopSoundEvent is not null && CanTrigger )
		{
			var sound = Sound.Play( StopSoundEvent, WorldPosition );
			sound.Volume = Volume;
		}
		_moveSound?.Stop();
		_moveSound = null;
	}
}
