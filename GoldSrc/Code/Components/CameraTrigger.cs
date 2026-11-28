namespace GoldSrc.Maps;

[Title( "GoldSrc Camera Trigger" )]
public sealed class CameraTrigger : MapEntity, ICameraModifier
{
	[Property] public float Wait { get; set; }
	[Property] public string Path { get; set; }
	[Property] public float Speed { get; set; }
	[Property] public float Acceleration { get; set; }
	[Property] public float Deceleration { get; set; }
	[Property] public bool StartAtPlayer { get; set; }
	[Property] public bool LookAtPlayer { get; set; }
	[Property] public bool TakeControl { get; set; }
	[Sync( SyncFlags.FromHost ), Change( nameof( ChangeViewer ) )] public PlayerController Viewer { get; private set; }

	GameObject _lookAt;
	PathNode _node;
	Vector3 _velocity, _direction;
	float _speed, _targetSpeed, _remaining;
	TimeUntil _return, _stop;
	PlayerController _locked;
	bool? _input;
	bool _look;
	System.IDisposable _beforeInput, _afterInput;

	public override void Trigger( GameObject activator = null )
	{
		if ( Viewer.IsValid() ) TurnOff( activator );
		else TurnOn( activator );
	}

	public override void TurnOn( GameObject activator = null )
	{
		if ( !Networking.IsHost || Viewer.IsValid() ) return;
		var player = activator?.GetComponentInParent<PlayerController>() ?? Scene.GetAllComponents<PlayerController>().FirstOrDefault();
		if ( !player.IsValid() ) return;
		_lookAt = LookAtPlayer ? player.GameObject : Map.Find( Target )?.GameObject;
		if ( !_lookAt.IsValid() ) return;

		foreach ( var previous in Scene.GetAllComponents<CameraTrigger>() )
			if ( previous.Viewer == player ) previous.TurnOff();

		_velocity = Vector3.Zero;
		if ( StartAtPlayer )
		{
			WorldPosition = player.EyeTransform.Position;
			WorldRotation = player.EyeTransform.Rotation;
			_velocity = player.Velocity;
		}
		_speed = _targetSpeed = Speed;
		_remaining = 0;
		_node = Map.Find( Path ) as PathNode;
		if ( _node.IsValid() && _node.Speed != 0 ) _targetSpeed = _node.Speed;
		_stop = _node?.Wait ?? 0;
		_return = Wait;
		Viewer = player;
		AdvancePath();
	}

	public override void TurnOff( GameObject activator = null )
	{
		if ( !Networking.IsHost || !Viewer.IsValid() ) return;
		Viewer = null;
		Fire( OnTrigger, GameObject );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !Viewer.IsValid() ) return;
		if ( _return <= 0 || !_lookAt.IsValid() )
		{
			TurnOff();
			return;
		}
		var delta = _lookAt.WorldPosition - WorldPosition;
		if ( !delta.IsNearlyZero() )
		{
			var angles = WorldRotation.Angles();
			var difference = (delta.EulerAngles - angles).Normal;
			WorldRotation = Rotation.From( angles + difference * (40 * Time.Delta * Time.Delta) );
		}
		if ( !TakeControl )
		{
			_velocity *= 0.8f;
			if ( _velocity.Length < 10 ) _velocity = Vector3.Zero;
		}
		AdvancePath();
		WorldPosition += _velocity * Time.Delta;
	}

	void AdvancePath()
	{
		if ( !_node.IsValid() ) return;
		_remaining -= _speed * Time.Delta;
		if ( _remaining <= 0 )
		{
			_node.Pass( GameObject );
			_node = _node.Next;
			if ( !_node.IsValid() )
			{
				_velocity = Vector3.Zero;
				return;
			}
			var offset = _node.WorldPosition - WorldPosition;
			_remaining = offset.Length;
			_direction = offset.Normal;
			if ( _node.Speed != 0 ) _targetSpeed = _node.Speed;
			_stop = _node.Wait;
		}
		var stopping = _stop > 0;
		var rate = stopping ? Deceleration : Acceleration;
		_speed = _speed.Approach( stopping ? 0 : _targetSpeed, (rate == 0 ? 500 : rate) * Time.Delta );
		_velocity = Vector3.Lerp( _velocity, _direction * _speed, 2 * Time.Delta );
	}

	void ChangeViewer( PlayerController previous, PlayerController current )
	{
		ReleaseControl();
		if ( !TakeControl || !current.IsValid() || current.IsProxy ) return;
		_locked = current;
		_beforeInput = Scene.AddHook( GameObjectSystem.Stage.StartFixedUpdate, 0, SuspendInput, nameof( CameraTrigger ), "Suspend viewer movement" );
		_afterInput = Scene.AddHook( GameObjectSystem.Stage.FinishFixedUpdate, 0, RestoreInput, nameof( CameraTrigger ), "Restore viewer movement" );
		_look = current.UseLookControls;
		current.UseLookControls = false;
		current.WishVelocity = Vector3.Zero;
	}

	void SuspendInput()
	{
		if ( !_locked.IsValid() ) return;
		_input = _locked.UseInputControls;
		_locked.UseInputControls = false;
		_locked.WishVelocity = Vector3.Zero;
	}

	void RestoreInput()
	{
		if ( _input.HasValue && _locked.IsValid() ) _locked.UseInputControls = _input.Value;
		_input = null;
	}

	void ReleaseControl()
	{
		_beforeInput?.Dispose();
		_afterInput?.Dispose();
		_beforeInput = _afterInput = null;
		RestoreInput();
		if ( !_locked.IsValid() ) return;
		_locked.UseLookControls = _look;
		_locked = null;
	}

	protected override void OnDisabled() => ReleaseControl();

	int ICameraModifier.CameraOrder => 300;
	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( !Viewer.IsValid() || Viewer.IsProxy || camera != Scene.Camera ) return;
		view.Position = WorldPosition;
		view.Rotation = WorldRotation;
	}
}
