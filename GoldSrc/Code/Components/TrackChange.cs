using Sandbox;

namespace GoldSrc.Maps;

[Title( "GoldSrc Track Change" )]
[Category( "GoldSrc" )]
public sealed class TrackChange : PlatformBrush
{
	[Property] public PathNode TopTrack { get; set; }
	[Property] public PathNode BottomTrack { get; set; }
	[Property] public TrackTrain Train { get; set; }
	[Property] public bool Automatic { get; set; }
	[Property] public bool DontMove { get; set; }
	[Property] public SoundEvent AlarmSound { get; set; }
	protected override bool RotationOnly => DontMove;
	private PlatformState _targetState;
	private enum TrainState { Safe, Blocking, Following }
	private TrainState _trainState;
	private float _findAt;
	private bool _found;
	private Vector3 _trainStart;
	private Vector3 _trainEnd;
	private Angles _trainAngles;
	private Angles _trainAngleOffset;

	protected override void OnStart()
	{
		base.OnStart();
		UseAvailable = true;
		_targetState = StartAtTop ? PlatformState.Bottom : PlatformState.Top;
		_findAt = Time.Now + 2f;
	}

	protected override void OnFixedUpdate()
	{
		if ( !_found && Time.Now >= _findAt )
		{
			_found = true;
			var collider = GetComponent<Collider>();
			var center = collider.IsValid() ? collider.GetWorldBounds().Center : WorldPosition;
			if ( TopTrack.IsValid() ) TopTrack = TopTrack.Nearest( center );
			if ( BottomTrack.IsValid() ) BottomTrack = BottomTrack.Nearest( center );
			UpdateTracks();
		}
		base.OnFixedUpdate();
	}

	public override void Trigger( GameObject activator = null ) => UseTrack( activator, UseType.Toggle );
	public override void TurnOn( GameObject activator = null ) => UseTrack( activator, UseType.On );
	public override void TurnOff( GameObject activator = null ) => UseTrack( activator, UseType.Off );
	public override void SetValue( float value, GameObject activator = null ) => UseTrack( activator, UseType.Set );

	private void UseTrack( GameObject activator, UseType use )
	{
		if ( !CanTrigger || !UseAvailable || Moving ) return;
		var current = State == PlatformState.Top ? TopTrack : BottomTrack;
		if ( Automatic && (!activator.IsValid() || !activator.GetComponent<TrackTrain>().IsValid()) )
		{
			var next = current.IsValid() ? current.NextEnabled( true, false ) : null;
			var on = _targetState == PlatformState.Bottom;
			if ( next.IsValid() && Train.IsValid() && Train.CurrentPath != next
				&& (use is UseType.Toggle or UseType.Set || (use == UseType.On) != on) )
				_targetState = on ? PlatformState.Top : PlatformState.Bottom;
			UpdateTracks();
			return;
		}
		_trainState = EvaluateTrain( current );
		if ( Automatic && (_trainState != TrainState.Following || State == _targetState) ) return;
		if ( _trainState == TrainState.Blocking )
		{
			if ( AlarmSound is not null ) Sound.Play( AlarmSound, WorldPosition );
			return;
		}
		UseAvailable = false;
		Begin( State == PlatformState.Bottom );
	}

	private TrainState EvaluateTrain( PathNode current )
	{
		if ( !current.IsValid() || !Train.IsValid() ) return TrainState.Safe;
		var path = Train.CurrentPath;
		if ( path != current && path != current.Previous && path != current.Next ) return TrainState.Safe;
		if ( Train.Speed != 0f ) return TrainState.Blocking;
		var distance = (WorldPosition - Train.WorldPosition).WithZ( 0f ).Length;
		if ( distance < Train.WheelBase ) return TrainState.Following;
		return distance > 150f + Train.WheelBase ? TrainState.Safe : TrainState.Blocking;
	}

	protected override void OnBeginMove( bool up, float duration )
	{
		UpdateTracks();
		if ( _trainState != TrainState.Following || !Train.IsValid() ) return;
		_trainStart = Train.WorldPosition;
		_trainAngles = Train.WorldRotation.Angles();
		_trainAngleOffset = (up ? AngleOffset : default) - CurrentAngles;
		var offset = _trainStart - WorldPosition;
		_trainEnd = (up ? TopPosition : BottomPosition) + Rotation.From( _trainAngleOffset ) * offset;
		Train.SuspendTrack();
	}

	protected override void OnMove( float fraction )
	{
		if ( _trainState != TrainState.Following || !Train.IsValid() ) return;
		Train.WorldPosition = Vector3.Lerp( _trainStart, _trainEnd, fraction );
		Train.WorldRotation = Rotation.From( _trainAngles + _trainAngleOffset * fraction );
	}

	protected override void Arrive( bool up )
	{
		base.Arrive( up );
		if ( _trainState == TrainState.Following && Train.IsValid() )
			Train.SetTrack( up ? TopTrack : BottomTrack );
		ReturnAt = float.PositiveInfinity;
		UpdateTracks();
		UseAvailable = true;
	}

	private void UpdateTracks()
	{
		if ( !TopTrack.IsValid() || !BottomTrack.IsValid() ) return;
		if ( !Automatic )
		{
			TopTrack.Disabled = State != PlatformState.Top;
			BottomTrack.Disabled = State != PlatformState.Bottom;
			return;
		}
		var selected = (_targetState == PlatformState.Top ? TopTrack : BottomTrack).NextEnabled( true, false );
		var other = (_targetState == PlatformState.Top ? BottomTrack : TopTrack).NextEnabled( true, false );
		if ( selected.IsValid() )
		{
			selected.Disabled = false;
			if ( _trainState == TrainState.Following && Train.IsValid() && Train.Speed == 0f )
				Train.Use( GameObject, this, (int)UseType.On, 0f );
		}
		if ( other.IsValid() ) other.Disabled = true;
	}
}
