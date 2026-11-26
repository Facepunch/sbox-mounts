namespace Source1.GarrysMod;

/// <summary>The Physics Gun carries the point under the crosshair, including individual ragdoll bodies.</summary>
public sealed class PhysicsGun : Weapon
{
	const float MinRange = 40;

	[ConVar( "physgun_maxrange", ConVarFlags.Server | ConVarFlags.Replicated, Min = 128, Max = 32768, Help = "Maximum carry distance. The Physics Gun can pick up objects twice this far away." )]
	public static float MaxRange { get; set => field = float.IsFinite( value ) ? value : 4096; } = 4096;

	[ConVar( "physgun_wheelspeed", Help = "Physics Gun carry distance per scroll notch." )]
	public static float WheelSpeed { get; set; } = 10;

	enum Trigger { Released, Grab, Adjust, Push, Pull, Freeze }
	System.IDisposable _movementHook;
	[Sync] Trigger InputState { get; set; }
	[Sync( SyncFlags.FromHost )] public Rigidbody HeldBody { get; private set; }
	Vector3 _localPoint;
	Rotation _yawOffset;
	float _distance;
	float _arrivalTime;

	public PhysicsGun()
	{
		UsesAmmo = false;
		AttackDamageTags = [];
	}

	protected override void OnControl()
	{
		if ( NextPrimaryFire > 0 ) return;
		if ( Input.Pressed( "attack2" ) && HeldBody.IsValid() )
		{
			InputState = Trigger.Freeze;
			SetNextFire( 0.5f );
			return;
		}

		InputState = Input.Down( "attack1" ) && !Input.Down( "attack2" ) ? Trigger.Grab : Trigger.Released;
		if ( InputState != Trigger.Grab || !HeldBody.IsValid() ) return;

		if ( Input.Down( "use" ) )
		{
			var forward = Input.AnalogMove.x;
			InputState = forward switch { > 0 => Trigger.Push, < 0 => Trigger.Pull, _ => Trigger.Adjust };
			_movementHook ??= Scene.AddHook( GameObjectSystem.Stage.PhysicsStep, -1, RedirectMovement,
				nameof( PhysicsGun ), "Redirect player movement while adjusting a held body" );
		}

		var wheel = Input.MouseWheel.y;
		if ( wheel == 0 ) return;
		var speed = float.IsFinite( WheelSpeed ) ? WheelSpeed.Clamp( -10000, 10000 ) : 10;
		Scroll( HeldBody, wheel * (System.MathF.Abs( speed ) < 0.1f ? 0.1f : speed) );
		Input.MouseWheel = default;
		Input.ReleaseAction( "SlotNext" );
		Input.ReleaseAction( "SlotPrev" );
	}

	// The controller builds its wish velocity in fixed update, after frame input handling.
	void RedirectMovement()
	{
		if ( !IsProxy && Active && HasOwner && HeldBody.IsValid() && InputState is Trigger.Adjust or Trigger.Push or Trigger.Pull )
			Owner.WishVelocity = Vector3.Zero;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Scroll( Rigidbody body, float offset )
	{
		if ( Active && HeldBody.IsValid() && HeldBody == body && float.IsFinite( offset ) )
			_distance = (_distance + offset).Clamp( MinRange, MaxRange );
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !Networking.IsHost ) return;
		if ( !Active || !HasOwner || InputState is Trigger.Released or Trigger.Freeze )
		{
			if ( Active && HasOwner && InputState == Trigger.Freeze && HeldBody.IsValid() )
				HeldBody.MotionEnabled = false;
			HeldBody = null;
			return;
		}

		var ray = AimRay;
		var yaw = Rotation.FromYaw( Owner.EyeAngles.yaw );
		if ( !HeldBody.IsValid() )
		{
			var hit = Scene.Trace.Ray( ray, MaxRange * 2 ).IgnoreGameObjectHierarchy( GameObject.Root )
				.WithoutTags( "trigger", "projectile" ).Run();
			var body = hit.GameObject?.GetComponentInParent<Rigidbody>();
			if ( !body.IsValid() || body.IsProxy || !body.PhysicsBody.IsValid() || body.GetComponentInParent<PlayerController>().IsValid() ) return;

			HeldBody = body;
			body.MotionEnabled = true;
			var transform = body.PhysicsBody.Transform;
			_localPoint = transform.PointToLocal( hit.HitPosition );
			_distance = ray.Position.Distance( hit.HitPosition );
			_yawOffset = yaw.Inverse * transform.Rotation;
			_arrivalTime = body.GetComponentInParent<ModelPhysics>().IsValid() ? 0.1f : 0.05f;
		}

		var physics = HeldBody.PhysicsBody;
		if ( !physics.IsValid() )
		{
			HeldBody = null;
			return;
		}
		if ( InputState is Trigger.Push or Trigger.Pull )
			_distance = _distance.Approach( InputState == Trigger.Push ? MaxRange : MinRange, 100 * Time.Delta );

		var position = ray.Position + ray.Forward * _distance - physics.Rotation * _localPoint;
		var velocity = ShadowController.Velocity( physics.Velocity, position - physics.Position, 5000, 10000, _arrivalTime, 0.8f );
		physics.Velocity = velocity.ClampLength( 4000 );
		var angularError = ShadowController.AngularError( physics.Rotation, yaw * _yawOffset );
		physics.AngularVelocity = ShadowController.Velocity( physics.AngularVelocity, angularError, 5000f.DegreeToRadian(), 10000f.DegreeToRadian(), _arrivalTime, 0.8f );
	}

	void StopRedirectingMovement()
	{
		_movementHook?.Dispose();
		_movementHook = null;
	}

	protected override void OnDisabled()
	{
		StopRedirectingMovement();
		base.OnDisabled();
	}

	protected override void OnHolstered()
	{
		StopRedirectingMovement();
		if ( !IsProxy ) InputState = Trigger.Released;
		if ( Networking.IsHost ) HeldBody = null;
		base.OnHolstered();
	}
}
