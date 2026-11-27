namespace UnityMount;

/// <summary>
/// Interactive hinged door created from Unity 'Door' / 'DoubleDoor' MonoBehaviour GameObjects.
/// Press Use (E) to swing smoothly open/closed away from the player, or shoot/melee to kick it open!
/// </summary>
[Title( "Unity Door" ), Category( "Unity Mount" ), Icon( "door_front" )]
public sealed class UnityDoor : Component, Component.IPressable, Component.IDamageable
{
	[Property] public float OpenAngle { get; set; } = 95f;
	[Property] public float OpenDuration { get; set; } = 0.36f;
	[Property] public float KickDuration { get; set; } = 0.18f;
	[Property] public float SwingSign { get; set; } = 1f;
	[Property] public bool IsMetal { get; set; }
	[Property] public string OpenSoundPath { get; set; } = "mount://pbd/sounds/master/pbd_env_door-001.vsnd";
	[Property] public string CloseSoundPath { get; set; } = "mount://pbd/sounds/master/pbd_env_doorclose_wood-001.vsnd";
	[Property] public UnityDoor PartnerDoor { get; set; }

	Rotation _closedRot;
	bool _initialized;
	bool _isOpen;
	float _currentAngle;
	float _startAngle;
	float _targetAngle;
	float _animTime;
	float _animDuration = 0.36f;
	bool _animating;

	protected override void OnStart()
	{
		EnsureInitialized();
	}

	void EnsureInitialized()
	{
		if ( _initialized ) return;
		_closedRot = LocalRotation;
		_initialized = true;
	}

	public bool CanPress( IPressable.Event e ) => true;

	public IPressable.Tooltip? GetTooltip( IPressable.Event e )
		=> new IPressable.Tooltip( _isOpen ? "Close Door" : "Open Door", "door_front", null );

	public bool Press( IPressable.Event e )
	{
		var presserPos = e.Source.IsValid() ? e.Source.WorldPosition : WorldPosition;
		Toggle( presserPos, fastKick: false, syncPartner: true );
		return true;
	}

	public void OnDamage( in DamageInfo damage )
	{
		// Shooting or shoveling a closed door blasts it wide open POSTAL-style!
		if ( !_isOpen )
		{
			var origin = damage.Attacker.IsValid() ? damage.Attacker.WorldPosition : damage.Position;
			OpenAwayFrom( origin, fastKick: true, syncPartner: true );
		}
	}

	public void Toggle( Vector3 actorWorldPos, bool fastKick, bool syncPartner )
	{
		EnsureInitialized();
		if ( _isOpen )
			Close( fastKick, syncPartner );
		else
			OpenAwayFrom( actorWorldPos, fastKick, syncPartner );
	}

	public void OpenAwayFrom( Vector3 actorWorldPos, bool fastKick, bool syncPartner )
	{
		EnsureInitialized();
		float dir = SwingSign;

		// For single doors, swing away from the player based on which side of the door plane they stand on
		if ( !PartnerDoor.IsValid() )
		{
			var localActor = WorldTransform.PointToLocal( actorWorldPos );
			// Determine dominant door plane normal from renderer bounds (thin axis = door thickness)
			var mr = GetComponent<ModelRenderer>();
			if ( mr.IsValid() && mr.Model is not null )
			{
				var sz = mr.Model.Bounds.Size;
				var center = mr.Model.Bounds.Center;
				if ( sz.x < sz.y )
				{
					// Door spans along Y, thin along X: swing away from actor X side
					float side = localActor.x >= center.x ? 1f : -1f;
					float hingeSide = center.y >= 0f ? 1f : -1f;
					dir = side * hingeSide;
				}
				else
				{
					// Door spans along X, thin along Y: swing away from actor Y side
					float side = localActor.y >= center.y ? -1f : 1f;
					float hingeSide = center.x >= 0f ? 1f : -1f;
					dir = side * hingeSide;
				}
			}
		}

		_isOpen = true;
		_startAngle = _currentAngle;
		_targetAngle = OpenAngle * dir;
		_animDuration = fastKick ? KickDuration : OpenDuration;
		_animTime = 0f;
		_animating = true;

		string snd = IsMetal
			? "mount://pbd/sounds/master/pbd_env_door_metal-001.vsnd"
			: OpenSoundPath;
		UnitySoundUtil.PlayAt( snd, WorldPosition, fastKick ? 1.0f : 0.78f, 1100f );

		if ( syncPartner && PartnerDoor.IsValid() && !PartnerDoor._isOpen )
			PartnerDoor.OpenAwayFrom( actorWorldPos, fastKick, syncPartner: false );
	}

	public void Close( bool fastKick, bool syncPartner )
	{
		EnsureInitialized();
		_isOpen = false;
		_startAngle = _currentAngle;
		_targetAngle = 0f;
		_animDuration = fastKick ? KickDuration : OpenDuration;
		_animTime = 0f;
		_animating = true;

		string snd = IsMetal
			? "mount://pbd/sounds/master/pbd_env_door_metal-002.vsnd"
			: CloseSoundPath;
		UnitySoundUtil.PlayAt( snd, WorldPosition, 0.72f, 1000f );

		if ( syncPartner && PartnerDoor.IsValid() && PartnerDoor._isOpen )
			PartnerDoor.Close( fastKick, syncPartner: false );
	}

	protected override void OnUpdate()
	{
		if ( !_animating )
			return;

		EnsureInitialized();
		_animTime += Time.Delta;
		float t = _animDuration > 0.001f ? Math.Clamp( _animTime / _animDuration, 0f, 1f ) : 1f;
		// Smooth cubic ease-out
		float eased = 1f - MathF.Pow( 1f - t, 3f );
		_currentAngle = _startAngle + (_targetAngle - _startAngle) * eased;
		LocalRotation = _closedRot * Rotation.FromYaw( _currentAngle );

		if ( t >= 1f )
		{
			_currentAngle = _targetAngle;
			LocalRotation = _closedRot * Rotation.FromYaw( _currentAngle );
			_animating = false;
		}
	}
}
