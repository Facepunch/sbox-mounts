namespace Source1.GarrysMod;

/// <summary>The Physics Gun carries the point under the crosshair, including individual ragdoll bodies.</summary>
public sealed partial class PhysicsGun : Weapon, PlayerController.IEvents
{
	const float MinRange = 40;
	const string GridConVar = "gm_snapgrid";
	const float CarryMass = 45678;
	const string KeepMassTag = "physgun_keep_mass";
	internal const string UnfreezableTag = "physgun_unfreezable";
	const string DisabledTag = "physgun_disabled";
	const string LimitedTag = "physgun_limited";

	[ConVar( "physgun_limited", ConVarFlags.Server | ConVarFlags.Replicated, Help = "Restrict Physics Gun pickup of map entities." )]
	public static bool Limited { get; set; }

	internal static void TagMapEntity( GameObject target, MapEntity entity )
	{
		if ( entity.Get( "gmod_allowphysgun" ) == "0" )
		{
			target.Tags.Add( DisabledTag );
			return;
		}

		var name = entity.Get( "classname" ).ToLowerInvariant();
		if ( name is "prop_vehicle_jeep" or "prop_vehicle_apc" or "prop_vehicle_jeep_old" )
			target.Tags.Add( KeepMassTag );
		bool limited = name == "func_physbox" ? (entity.Integer( "spawnflags" ) & 0x8000) != 0
			: name.StartsWith( "func_" ) || name.StartsWith( "prop_dynamic" ) || name.StartsWith( "prop_door" )
			|| (name.StartsWith( "prop_" ) && (entity.Integer( "spawnflags" ) & (8 | 512)) != 0);
		if ( limited ) target.Tags.Add( LimitedTag );
	}

	[ConVar( "physgun_maxrange", ConVarFlags.Server | ConVarFlags.Replicated, Min = 128, Max = 32768, Help = "Maximum carry distance. The Physics Gun can pick up objects twice this far away." )]
	public static float MaxRange { get; set => field = float.IsFinite( value ) ? value : 4096; } = 4096;

	[ConVar( "physgun_maxSpeed", ConVarFlags.Server, Min = 0 )]
	public static float MaxCarrySpeed { get; set; } = 5000;

	[ConVar( "physgun_maxSpeedDamping", ConVarFlags.Server, Min = 0 )]
	public static float MaxCarryDamping { get; set; } = 10000;

	[ConVar( "physgun_maxAngular", ConVarFlags.Server, Min = 0 )]
	public static float MaxAngularSpeed { get; set; } = 5000;

	[ConVar( "physgun_maxAngularDamping", ConVarFlags.Server, Min = 0 )]
	public static float MaxAngularDamping { get; set; } = 10000;

	[ConVar( "physgun_DampingFactor", ConVarFlags.Server )]
	public static float DampingFactor { get; set; } = 0.8f;

	[ConVar( "physgun_teleportDistance", ConVarFlags.Server, Min = 0, Help = "Snap a held body to its target when farther than this distance. Zero disables snapping." )]
	public static float TeleportDistance { get; set; }

	[ConVar( "physgun_timeToArrive", ConVarFlags.Server, Min = 0 )]
	public static float TimeToArrive { get; set; } = 0.05f;

	[ConVar( "physgun_timeToArriveRagdoll", ConVarFlags.Server, Min = 0 )]
	public static float RagdollTimeToArrive { get; set; } = 0.1f;

	[ConVar( "physgun_wheelspeed", Help = "Physics Gun carry distance per scroll notch." )]
	public static float WheelSpeed { get; set; } = 10;

	[ConVar( "phys_spinspeed", ConVarFlags.Server, Help = "Degrees per second to turn a held body with the movement keys." )]
	public static float SpinSpeed { get; set => field = float.IsFinite( value ) ? value : 200; } = 200;

	[ConVar( GridConVar, ConVarFlags.Saved, Min = 0, Max = 4096, Help = "World grid spacing for held props. Zero disables snapping." )]
	public static float SnapGrid { get; set; }

	[ConVar( "gm_snapangles", ConVarFlags.Saved, Min = -360, Max = 360, Help = "Angle increment for Physics Gun rotation while holding Use and Run." )]
	public static float SnapAngles { get; set; } = 45;

	[ConVar( "physgun_rotation_sensitivity", ConVarFlags.Saved, Min = 0.01f, Max = 10000, Help = "Sensitivity of mouse rotation while holding Use with the Physics Gun." )]
	public static float RotationSensitivity { get; set; } = 0.05f;

	enum Trigger { Released, Grab, Adjust, Push, Pull, Freeze }
	System.IDisposable _movementHook;
	[Sync] Trigger InputState { get; set; }
	bool Adjusting => InputState is Trigger.Adjust or Trigger.Push or Trigger.Pull;
	[Sync] float GridSize { get; set; }
	[Sync] float SnapStep { get; set; } = -1;
	float? _previousSnapStep;
	// Accumulate on the owner so a network update can carry several frames of mouse input.
	[Sync] Rotation MouseRotation { get; set; } = Rotation.Identity;
	Rotation _lastMouseRotation;
	[Sync] int SpinDirection { get; set; }
	[Sync( SyncFlags.FromHost )] Component HeldTarget { get; set; }
	public Rigidbody HeldBody => HeldTarget as Rigidbody;
	[Sync( SyncFlags.FromHost )] Vector3 GrabPoint { get; set; }
	[Sync( SyncFlags.FromHost )] bool BeamOn { get; set; }
	Rotation _yawOffset;
	float _distance;
	float _arrivalTime;
	(float Setting, float NativeOverride)? _previousMass;
	internal override bool ShouldPlayIdle => false;

	public PhysicsGun()
	{
		UsesAmmo = false;
		AttackDamageTags = [];
	}

	protected override void OnControl()
	{
		if ( Input.Pressed( "reload" ) )
		{
			Unfreeze();
			return;
		}

		if ( NextPrimaryFire > 0 ) return;
		if ( Input.Pressed( "attack2" ) && HeldTarget.IsValid() )
		{
			InputState = Trigger.Freeze;
			(WeaponModel as ViewModel)?.PlayPhyscannonAttack();
			SetNextFire( 0.5f );
			return;
		}

		InputState = Input.Down( "attack1" ) && !Input.Down( "attack2" ) ? Trigger.Grab : Trigger.Released;
		if ( InputState == Trigger.Grab ) GridSize = ConsoleSystem.GetValue( GridConVar ).ToFloat();
		if ( InputState == Trigger.Grab && Input.Pressed( "attack1" ) && !HeldTarget.IsValid() && TraceTarget( AimRay, MaxRange * 2 ).Hit )
			(WeaponModel as ViewModel)?.PlayPhyscannonAttack();
		if ( InputState != Trigger.Grab || !HeldTarget.IsValid() ) return;

		if ( Input.Down( "use" ) )
		{
			SnapStep = Input.Down( "run" ) ? System.MathF.Abs( float.IsFinite( SnapAngles ) ? SnapAngles : 45 ) : -1;
			var forward = Input.AnalogMove.x;
			SpinDirection = -System.Math.Sign( Input.AnalogMove.y );
			InputState = forward switch { > 0 => Trigger.Push, < 0 => Trigger.Pull, _ => Trigger.Adjust };
			_movementHook ??= Scene.AddHook( GameObjectSystem.Stage.PhysicsStep, -1, RedirectMovement,
				nameof( PhysicsGun ), "Redirect player movement while adjusting a held body" );
		}

		var wheel = Input.MouseWheel.y;
		if ( wheel == 0 ) return;
		var speed = float.IsFinite( WheelSpeed ) ? WheelSpeed.Clamp( -10000, 10000 ) : 10;
		Scroll( HeldTarget, wheel * (System.MathF.Abs( speed ) < 0.1f ? 0.1f : speed) );
		Input.MouseWheel = default;
		Input.ReleaseAction( "SlotNext" );
		Input.ReleaseAction( "SlotPrev" );
	}

	void PlayerController.IEvents.OnEyeAngles( ref Angles look )
	{
		if ( IsProxy || !Active || !HeldTarget.IsValid() || !Input.Down( "attack1" ) || !Input.Down( "use" ) ) return;

		// Source's default mouse pitch and yaw convert one command count into 0.022 degrees of camera motion.
		const float degreesPerCount = 0.022f;
		float sensitivity = float.IsFinite( RotationSensitivity ) ? RotationSensitivity.Clamp( 0.01f, 1000 ) : 0.05f;
		var turn = new Angles( -look.pitch, -look.yaw, 0 ) * (sensitivity / degreesPerCount);
		if ( turn.pitch != 0 || turn.yaw != 0 )
			MouseRotation = (turn.ToRotation() * MouseRotation).Normal;
		look = default;
	}

	// The controller builds its wish velocity in fixed update, after frame input handling.
	void RedirectMovement()
	{
		if ( !IsProxy && Active && HasOwner && HeldTarget.IsValid() && Adjusting )
			Owner.WishVelocity = Vector3.Zero;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Unfreeze()
	{
		if ( !Active || !HasOwner ) return;
		int count = Owner.GetComponent<FrozenProps>()?.Unfreeze( AimRay ) ?? 0;
		if ( count > 0 ) NotifyUnfrozen( count );
	}

	[Rpc.Owner( NetFlags.HostOnly | NetFlags.Reliable )]
	void NotifyUnfrozen( int count )
	{
		var text = Game.Language.GetPhrase( "physgun.unfrozen", new() { { "count", count.ToString() } } );
		Sandbox.UI.Notices.AddNotice( "ac_unit", Color.Cyan, text, 3 );
		var file = SoundFile.Load( "mount://gmod/sound/npc/roller/mine/rmine_chirp_answer1.wav.vsnd" );
		if ( file.IsValid() ) Sound.Play( new SoundEvent { UI = true, Sounds = [file] } );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Scroll( Component target, float offset )
	{
		if ( Active && HeldTarget.IsValid() && HeldTarget == target && float.IsFinite( offset ) )
			_distance = (_distance + offset).Clamp( MinRange, MaxRange );
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !Networking.IsHost ) return;
		if ( !Active || !HasOwner || InputState is Trigger.Released or Trigger.Freeze )
		{
			if ( Active && HasOwner && InputState == Trigger.Freeze && HeldBody.IsValid() && !HeldBody.Tags.Has( UnfreezableTag ) )
			{
				HeldBody.MotionEnabled = false;
				Owner.GetOrAddComponent<FrozenProps>().Track( HeldBody );
			}
			Release();
			return;
		}

		var ray = AimRay;
		var yaw = Rotation.FromYaw( Owner.EyeAngles.yaw );
		if ( !HeldTarget.IsValid() )
		{
			var hit = TraceTarget( ray, MaxRange * 2 );
			if ( !hit.Hit ) return;
			BeamOn = true;
			var body = hit.GameObject?.GetComponentInParent<Rigidbody>();
			Component target = (Component)body ?? hit.GameObject?.GetComponentInParent<Projectile>();
			if ( !target.IsValid() || target.Tags.Has( DisabledTag ) || (Limited && target.Tags.Has( LimitedTag )) ) return;
			if ( !CanMove( target, Network.Owner ) || (body.IsValid() && !body.PhysicsBody.IsValid()) ) return;

			HeldTarget = target;
			_previousMass = null;
			EntityRoot( target ).RunEvent<IPhysgunEvent>( x => x.OnPhysgunPickup() );
			if ( !target.IsValid() || HeldTarget != target ) return;
			var transform = target.WorldTransform;
			if ( body.IsValid() )
			{
				if ( !body.PhysicsBody.IsValid() )
				{
					Release();
					return;
				}
				PrepareBody( body );
				transform = body.PhysicsBody.Transform;
			}

			GrabPoint = transform.PointToLocal( hit.HitPosition );
			_distance = ray.Position.Distance( hit.HitPosition );
			_yawOffset = yaw.Inverse * transform.Rotation;
			_previousSnapStep = null;
			_lastMouseRotation = MouseRotation;
		}

		if ( InputState is Trigger.Push or Trigger.Pull )
			_distance = _distance.Approach( InputState == Trigger.Push ? MaxRange : MinRange, 100 * Time.Delta );

		var physics = HeldBody?.PhysicsBody;
		if ( HeldTarget is Rigidbody && !physics.IsValid() )
		{
			Release();
			return;
		}
		var rotation = HoldRotation( yaw );
		var position = ray.Position + ray.Forward * _distance - (physics?.Rotation ?? rotation) * GrabPoint;
		if ( GridSize > 0 && float.IsFinite( GridSize ) ) position = position.SnapToGrid( System.MathF.Min( GridSize, 4096 ) );
		if ( HeldTarget is Projectile projectile )
		{
			projectile.WorldRotation = rotation;
			projectile.WorldPosition = CarryPosition( projectile, position );
			return;
		}
		var offset = position - physics.Position;
		if ( TeleportDistance > 0 && offset.LengthSquared > TeleportDistance * TeleportDistance )
		{
			physics.Transform = new Transform( position, rotation );
			physics.Velocity = Vector3.Zero;
		}
		else
		{
			var velocity = ShadowController.Velocity( physics.Velocity, offset, MaxCarrySpeed, MaxCarryDamping, _arrivalTime, DampingFactor );
			physics.Velocity = velocity.ClampLength( 4000 );
		}

		var angularError = ShadowController.AngularError( physics.Rotation, rotation );
		physics.AngularVelocity = ShadowController.Velocity( physics.AngularVelocity, angularError, MaxAngularSpeed.DegreeToRadian(), MaxAngularDamping.DegreeToRadian(), _arrivalTime, DampingFactor );
	}

	void PrepareBody( Rigidbody body )
	{
		body.MotionEnabled = true;
		bool ragdoll = body.GetComponentInParent<ModelPhysics>().IsValid();
		// With two holders, the gun that raised the mass restores it on release.
		if ( !ragdoll && !body.Tags.Has( KeepMassTag ) && body.Mass < CarryMass )
		{
			var mass = body.Mass;
			var nativeOverride = 0f;
			if ( body.MassOverride <= 0 )
			{
				// ModelCollider can author the native mass while Rigidbody still uses automatic mass.
				body.PhysicsBody.Mass = 0;
				if ( body.Mass != mass ) nativeOverride = mass;
			}
			_previousMass = (body.MassOverride, nativeOverride);
			body.MassOverride = CarryMass;
		}
		_arrivalTime = ragdoll ? RagdollTimeToArrive : TimeToArrive;
	}

	Vector3 CarryPosition( Projectile projectile, Vector3 end )
	{
		var start = projectile.WorldPosition;
		SceneTraceResult Sweep( Vector3 from, Vector3 to ) =>
			projectile.FlightHull( Scene.Trace.Ray( from, to ).WithTag( "world" ).WithoutTags( "trigger" ) ).Run();

		var trace = Sweep( start, end );
		if ( trace.StartedSolid && !Sweep( end, end ).StartedSolid ) return end;
		// On obstruction Source retries the horizontal move at the entity's current height.
		return trace.Hit ? Sweep( start, end.WithZ( start.z ) ).EndPosition : end;
	}

	SceneTraceResult TraceTarget( Ray ray, float distance ) => Scene.Trace.Ray( ray, distance )
		.IgnoreGameObjectHierarchy( GameObject.Root ).WithoutTags( "trigger" ).Run();

	// Map props share a scene root. Dispatch to the mounted entity that owns this physics body.
	internal static GameObject EntityRoot( Component body )
	{
		var target = body.GameObject;
		while ( !target.IsRoot && !target.Tags.Has( "source1_entity", includeAncestors: false ) && target.Parent is { } parent )
			target = parent;
		return target;
	}

	internal static bool CanMove( Component body, Connection grabber )
	{
		if ( !body.IsValid() || body.IsProxy || body.GetComponentInParent<PlayerController>().IsValid() ) return false;
		var grab = new IPhysgunEvent.GrabEvent { Grabber = grabber };
		EntityRoot( body ).RunEvent<IPhysgunEvent>( x => x.OnPhysgunGrab( grab ) );
		return !grab.Cancelled && body.IsValid();
	}

	void Release()
	{
		var target = HeldTarget;
		var body = HeldBody;
		var previousMass = _previousMass;
		_previousMass = null;
		HeldTarget = null;
		BeamOn = false;
		if ( target.IsValid() ) EntityRoot( target ).RunEvent<IPhysgunEvent>( x => x.OnPhysgunDrop() );
		if ( previousMass is { } mass && body.IsValid() )
		{
			body.MassOverride = mass.Setting;
			if ( mass.NativeOverride > 0 && body.PhysicsBody.IsValid() ) body.PhysicsBody.Mass = mass.NativeOverride;
		}
	}

	Rotation HoldRotation( Rotation yaw )
	{
		if ( Adjusting && SpinDirection != 0 )
			_yawOffset = Rotation.FromYaw( SpinDirection * SpinSpeed * Time.Delta ) * _yawOffset;

		if ( !_lastMouseRotation.Equals( MouseRotation ) )
		{
			var delta = Rotation.Difference( _lastMouseRotation, MouseRotation );
			_yawOffset = (delta * _yawOffset).Normal;
			_lastMouseRotation = MouseRotation;
		}

		var target = yaw * _yawOffset;
		bool snapping = Adjusting && SnapStep >= 0;
		if ( !snapping && !_previousSnapStep.HasValue ) return target;

		float step = snapping ? SnapStep : _previousSnapStep.Value;
		target = step == 0 ? Rotation.Identity : target.Angles().SnapToGrid( step ).ToRotation();
		_previousSnapStep = snapping ? step : null;

		// Resume free rotation from the last grid angle.
		if ( !snapping ) _yawOffset = yaw.Inverse * target;
		return target;
	}

	void StopRedirectingMovement()
	{
		_movementHook?.Dispose();
		_movementHook = null;
	}

	protected override void OnDisabled()
	{
		if ( Networking.IsHost ) Release();
		ClearBeam();
		StopRedirectingMovement();
		base.OnDisabled();
	}

	protected override void OnHolstered()
	{
		StopRedirectingMovement();
		if ( !IsProxy ) InputState = Trigger.Released;
		if ( Networking.IsHost ) Release();
		ClearBeam();
		base.OnHolstered();
	}
}
