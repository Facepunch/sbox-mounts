namespace IdTech3;

public enum MoverKind { Door, Button, Plat, RotatingDoor }

/// <summary>
/// The idTech 3 "binary mover" (func_door, func_button, func_plat, RTCW func_door_rotating):
/// moves between a closed and an open position when touched, used or targeted.
/// </summary>
[Title( "idTech3 Mover" ), Category( "idTech3" ), Icon( "door_sliding" )]
public sealed class Mover : Component, IId3Usable, Component.IPressable
{
	[Property] public MoverKind Kind { get; set; }

	/// <summary>Offset from the closed position to the open position.</summary>
	[Property] public Vector3 Move { get; set; }

	/// <summary>Rotation (degrees) for rotating doors.</summary>
	[Property] public Angles Rotate { get; set; }

	[Property] public float Speed { get; set; } = 100;

	/// <summary>Seconds to stay open, -1 to stay open forever.</summary>
	[Property] public float Wait { get; set; } = 2;

	[Property] public bool StartOpen { get; set; }

	/// <summary>Movers with the same team name move together.</summary>
	[Property] public string Team { get; set; }

	/// <summary>Targets fired when the mover reaches its open position (buttons).</summary>
	[Property] public string Target { get; set; }

	/// <summary>Can be opened by pressing use as well as by touching.</summary>
	[Property] public bool Usable { get; set; } = true;

	/// <summary>Using an open mover closes it (RTCW TOGGLE / STAYOPEN), instead of keeping it open.</summary>
	[Property] public bool Toggle { get; set; }

	/// <summary>
	/// Rotating doors swing away from whoever opens them, as RTCW's do, instead of always the same way.
	/// </summary>
	[Property] public bool SwingAway { get; set; }

	/// <summary>Centre of the mover's brushes, relative to its pivot, for working out which way to swing.</summary>
	[Property] public Vector3 Center { get; set; }

	/// <summary>Played when it starts opening (and closing, without <see cref="CloseStartSound"/>).</summary>
	[Property, Group( "Sound" )] public SoundEvent StartSound { get; set; }
	/// <summary>Played when it's fully open (and closed, without <see cref="CloseStopSound"/>).</summary>
	[Property, Group( "Sound" )] public SoundEvent StopSound { get; set; }
	[Property, Group( "Sound" )] public SoundEvent CloseStartSound { get; set; }
	[Property, Group( "Sound" )] public SoundEvent CloseStopSound { get; set; }

	enum State { Closed, Opening, Open, Closing }

	State _state;
	float _fraction;
	TimeSince _openedAt;
	Vector3 _closedPosition;
	Rotation _closedRotation;
	bool _initialized;
	/// <summary>1 or -1: which way a rotating door is swinging this time.</summary>
	float _swing = 1;

	protected override void OnStart() => Init();

	void Init()
	{
		if ( _initialized ) return;
		_initialized = true;
		_closedPosition = LocalPosition;
		_closedRotation = LocalRotation;
		if ( StartOpen )
		{
			// START_OPEN: the open position is where it spawned
			_closedPosition -= Move;
			_closedRotation *= Rotation.From( Rotate ).Inverse;
			_fraction = 1;
			_state = State.Open;
			Wait = -1;
		}
		if ( Kind == MoverKind.Plat )
		{
			// plats spawn lowered
			_fraction = 1;
			_state = State.Open;
		}
		Apply();
	}

	float Duration
	{
		get
		{
			float speed = MathF.Max( Speed, 1 );
			if ( Kind == MoverKind.RotatingDoor )
			{
				float degrees = MathF.Max( MathF.Abs( Rotate.pitch ), MathF.Max( MathF.Abs( Rotate.yaw ), MathF.Abs( Rotate.roll ) ) );
				return MathF.Max( degrees / speed, 0.05f );
			}
			return MathF.Max( Move.Length / speed, 0.05f );
		}
	}

	IEnumerable<Mover> TeamMembers()
	{
		if ( string.IsNullOrEmpty( Team ) ) return [this];
		return Scene.GetAllComponents<Mover>().Where( m => m.Team == Team );
	}

	public void Use( GameObject activator )
	{
		foreach ( var m in TeamMembers() ) m.Activate( activator );
	}

	/// <summary>Called by the touch trigger.</summary>
	public void Touched( GameObject activator )
	{
		if ( Kind == MoverKind.Plat )
		{
			// riding a lowered plat raises it
			if ( _state == State.Open ) foreach ( var m in TeamMembers() ) m.StartClosing();
			else if ( _state == State.Closed ) _openedAt = 0;
			return;
		}
		Use( activator );
	}

	void Activate( GameObject activator )
	{
		Init();
		switch ( _state )
		{
			case State.Closed:
				ChooseSwing( activator );
				StartOpening();
				break;
			case State.Open:
				// toggle doors close when used again; others re-arm their wait while being touched
				if ( Toggle ) StartClosing();
				else _openedAt = 0;
				break;
			case State.Closing:
				if ( Kind != MoverKind.Button ) StartOpening();
				break;
		}
	}

	/// <summary>
	/// For <see cref="SwingAway"/> doors: swing the way that moves the door away from the activator, as RTCW picks
	/// between its two open positions.
	/// </summary>
	void ChooseSwing( GameObject activator )
	{
		_swing = 1;
		if ( !SwingAway || Kind != MoverKind.RotatingDoor || !activator.IsValid() ) return;

		var parent = GameObject.Parent;
		Vector3 CenterAt( float fraction )
		{
			var local = _closedPosition + _closedRotation * Rotation.From( Rotate * fraction ) * Center;
			return parent.IsValid() ? parent.WorldTransform.PointToWorld( local ) : local;
		}

		var closed = CenterAt( 0 );
		var opening = CenterAt( 0.05f ) - closed;
		if ( Vector3.Dot( opening, activator.WorldPosition - closed ) > 0 ) _swing = -1;
	}

	void StartOpening()
	{
		_state = State.Opening;
		PlaySound( StartSound );
	}

	void StartClosing()
	{
		_state = State.Closing;
		PlaySound( CloseStartSound ?? StartSound );
	}

	void PlaySound( SoundEvent sound )
	{
		if ( sound is not null ) Sound.Play( sound, WorldPosition );
	}

	protected override void OnFixedUpdate()
	{
		Init();
		float step = Time.Delta / Duration;
		switch ( _state )
		{
			case State.Opening:
				_fraction = MathF.Min( 1, _fraction + step );
				if ( _fraction >= 1 )
				{
					_state = State.Open;
					_openedAt = 0;
					PlaySound( StopSound );
					if ( Kind == MoverKind.Button ) Id3Entity.FireTargets( Scene, Target, GameObject );
				}
				break;

			case State.Open:
				if ( Kind == MoverKind.Plat ) break;
				if ( Wait >= 0 && _openedAt > Wait ) StartClosing();
				break;

			case State.Closing:
				_fraction = MathF.Max( 0, _fraction - step );
				if ( _fraction <= 0 )
				{
					_state = State.Closed;
					_openedAt = 0;
					PlaySound( CloseStopSound ?? StopSound );
				}
				break;

			case State.Closed:
				// plats return down after a second at the top
				if ( Kind == MoverKind.Plat && _openedAt > 1f ) StartOpening();
				break;
		}
		Apply();
	}

	void Apply()
	{
		if ( Kind == MoverKind.RotatingDoor )
		{
			LocalRotation = _closedRotation * Rotation.From( Rotate * (_fraction * _swing) );
			return;
		}
		LocalPosition = _closedPosition + Move * _fraction;
	}

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		if ( !Usable && Kind != MoverKind.Plat ) return null;

		return new IPressable.Tooltip( "Use", "touch_app", "" );
	}

	bool IPressable.CanPress( IPressable.Event e ) => Usable && Kind != MoverKind.Plat;

	bool IPressable.Press( IPressable.Event e )
	{
		Use( e.Source?.GameObject );
		return true;
	}
}

/// <summary>Trigger volume that opens a mover when a player is inside (spawned door triggers, buttons, plats).</summary>
[Title( "idTech3 Mover Trigger" ), Category( "idTech3" ), Icon( "sensor_door" )]
public sealed class MoverTrigger : Component, Component.ITriggerListener
{
	[Property] public Mover Mover { get; set; }

	readonly HashSet<Collider> _touching = [];

	void ITriggerListener.OnTriggerEnter( Collider other ) { _touching.Add( other ); Touch( other ); }
	void ITriggerListener.OnTriggerExit( Collider other ) => _touching.Remove( other );

	protected override void OnFixedUpdate()
	{
		_touching.RemoveWhere( x => !x.IsValid() );
		foreach ( var c in _touching.ToArray() ) Touch( c );
	}

	void Touch( Collider other )
	{
		if ( !Mover.IsValid() ) return;
		if ( Id3Entity.PlayerOf( other ) is { } pc ) Mover.Touched( pc.GameObject );
	}
}

/// <summary>func_rotating: constant rotation.</summary>
[Title( "idTech3 Rotating" ), Category( "idTech3" ), Icon( "autorenew" )]
public sealed class Rotating : Component
{
	/// <summary>Degrees per second around pitch / yaw / roll.</summary>
	[Property] public Angles Speed { get; set; } = new( 0, 100, 0 );

	Rotation _base;
	protected override void OnStart() => _base = LocalRotation;
	protected override void OnUpdate() => LocalRotation = _base * Rotation.From( Speed * Time.Now );
}

/// <summary>func_bobbing: sine wave movement along an axis.</summary>
[Title( "idTech3 Bobbing" ), Category( "idTech3" ), Icon( "swap_vert" )]
public sealed class Bobbing : Component
{
	[Property] public Vector3 Axis { get; set; } = Vector3.Up;
	[Property] public float Height { get; set; } = 32;
	/// <summary>Seconds per cycle.</summary>
	[Property] public float Period { get; set; } = 4;
	[Property] public float Phase { get; set; }

	Vector3 _base;
	protected override void OnStart() => _base = LocalPosition;
	protected override void OnUpdate()
	{
		float t = Time.Now / MathF.Max( Period, 0.01f ) + Phase;
		LocalPosition = _base + Axis * MathF.Sin( t * MathF.PI * 2 ) * Height;
	}
}

/// <summary>func_pendulum: swings around its origin (roll).</summary>
[Title( "idTech3 Pendulum" ), Category( "idTech3" ), Icon( "watch" )]
public sealed class Pendulum : Component
{
	/// <summary>Swing amplitude in degrees.</summary>
	[Property] public float Amplitude { get; set; } = 30;
	[Property] public float Frequency { get; set; } = 0.5f;
	[Property] public float Phase { get; set; }

	Rotation _base;
	protected override void OnStart() => _base = LocalRotation;
	protected override void OnUpdate()
	{
		float angle = Amplitude * MathF.Sin( (Time.Now * Frequency + Phase) * MathF.PI * 2 );
		LocalRotation = _base * Rotation.From( 0, 0, angle );
	}
}

/// <summary>func_train: moves through path_corner points.</summary>
[Title( "idTech3 Train" ), Category( "idTech3" ), Icon( "train" )]
public sealed class Train : Component, IId3Usable
{
	[Property] public List<Vector3> Path { get; set; } = [];
	[Property] public List<float> Waits { get; set; } = [];
	[Property] public List<float> Speeds { get; set; } = [];
	[Property] public float Speed { get; set; } = 100;
	[Property] public bool StartOn { get; set; } = true;
	[Property] public SoundEvent MoveSound { get; set; }

	int _segment;
	float _progress;
	float _waitUntil;
	bool _running;

	protected override void OnStart()
	{
		_running = StartOn;
		if ( Path.Count > 0 ) WorldPosition = Path[0];
	}

	public void Use( GameObject activator ) => _running = !_running;

	protected override void OnFixedUpdate()
	{
		if ( !_running || Path.Count < 2 ) return;
		if ( Time.Now < _waitUntil ) return;

		var from = Path[_segment % Path.Count];
		var to = Path[(_segment + 1) % Path.Count];
		float speed = _segment < Speeds.Count && Speeds[_segment] > 0 ? Speeds[_segment] : Speed;
		float length = MathF.Max( from.Distance( to ), 0.01f );
		_progress += Time.Delta * MathF.Max( speed, 1 ) / length;

		if ( _progress >= 1 )
		{
			_progress = 0;
			_segment = (_segment + 1) % Path.Count;
			WorldPosition = to;
			int corner = _segment;
			float wait = corner < Waits.Count ? Waits[corner] : 0;
			if ( wait < 0 ) { _running = false; return; }
			_waitUntil = Time.Now + wait;
			return;
		}
		WorldPosition = Vector3.Lerp( from, to, _progress );
	}
}
