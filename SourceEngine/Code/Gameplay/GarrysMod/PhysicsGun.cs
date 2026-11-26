namespace Source1.GarrysMod;

/// <summary>The Physics Gun carries the point under the crosshair, including individual ragdoll bodies.</summary>
public sealed partial class PhysicsGun : Weapon, PlayerController.IEvents
{
	const float MinRange = 40;
	const string GridConVar = "gm_snapgrid";
	const float CarryMass = 45678;
	const string KeepMassTag = "physgun_keep_mass";
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
	[Sync( SyncFlags.FromHost )] public Rigidbody HeldBody { get; private set; }
	[Sync( SyncFlags.FromHost )] Vector3 GrabPoint { get; set; }
	[Sync( SyncFlags.FromHost )] bool BeamOn { get; set; }
	Rotation _yawOffset;
	float _distance;
	float _arrivalTime;
	float? _previousMassOverride;
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
		if ( Input.Pressed( "attack2" ) && HeldBody.IsValid() )
		{
			InputState = Trigger.Freeze;
			(WeaponModel as ViewModel)?.PlayPhyscannonAttack();
			SetNextFire( 0.5f );
			return;
		}

		InputState = Input.Down( "attack1" ) && !Input.Down( "attack2" ) ? Trigger.Grab : Trigger.Released;
		if ( InputState == Trigger.Grab ) GridSize = ConsoleSystem.GetValue( GridConVar ).ToFloat();
		if ( InputState == Trigger.Grab && Input.Pressed( "attack1" ) && !HeldBody.IsValid() && TraceTarget( AimRay ).Hit )
			(WeaponModel as ViewModel)?.PlayPhyscannonAttack();
		if ( InputState != Trigger.Grab || !HeldBody.IsValid() ) return;

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
		Scroll( HeldBody, wheel * (System.MathF.Abs( speed ) < 0.1f ? 0.1f : speed) );
		Input.MouseWheel = default;
		Input.ReleaseAction( "SlotNext" );
		Input.ReleaseAction( "SlotPrev" );
	}

	void PlayerController.IEvents.OnEyeAngles( ref Angles look )
	{
		if ( IsProxy || !Active || !HeldBody.IsValid() || !Input.Down( "attack1" ) || !Input.Down( "use" ) ) return;

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
		if ( !IsProxy && Active && HasOwner && HeldBody.IsValid() && Adjusting )
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
			{
				HeldBody.MotionEnabled = false;
				Owner.GetOrAddComponent<FrozenProps>().Track( HeldBody );
			}
			Release();
			return;
		}

		var ray = AimRay;
		var yaw = Rotation.FromYaw( Owner.EyeAngles.yaw );
		if ( !HeldBody.IsValid() )
		{
			var hit = TraceTarget( ray );
			if ( !hit.Hit ) return;
			BeamOn = true;
			var body = hit.GameObject?.GetComponentInParent<Rigidbody>();
			if ( body.IsValid() && (body.Tags.Has( DisabledTag ) || (Limited && body.Tags.Has( LimitedTag ))) ) return;
			if ( !CanMove( body, Network.Owner ) || !body.PhysicsBody.IsValid() ) return;

			HeldBody = body;
			_previousMassOverride = null;
			body.GameObject.Root.RunEvent<IPhysgunEvent>( x => x.OnPhysgunPickup() );
			if ( !body.IsValid() || HeldBody != body || !body.PhysicsBody.IsValid() ) return;
			body.MotionEnabled = true;
			bool ragdoll = body.GetComponentInParent<ModelPhysics>().IsValid();
			// With two holders, the gun that raised the mass restores it on release.
			if ( !ragdoll && !body.Tags.Has( KeepMassTag ) && body.Mass < CarryMass )
			{
				_previousMassOverride = body.MassOverride;
				body.MassOverride = CarryMass;
			}
			var transform = body.PhysicsBody.Transform;
			GrabPoint = transform.PointToLocal( hit.HitPosition );
			_distance = ray.Position.Distance( hit.HitPosition );
			_yawOffset = yaw.Inverse * transform.Rotation;
			_previousSnapStep = null;
			_lastMouseRotation = MouseRotation;
			_arrivalTime = ragdoll ? RagdollTimeToArrive : TimeToArrive;
		}

		var physics = HeldBody?.PhysicsBody;
		if ( !physics.IsValid() )
		{
			Release();
			return;
		}
		if ( InputState is Trigger.Push or Trigger.Pull )
			_distance = _distance.Approach( InputState == Trigger.Push ? MaxRange : MinRange, 100 * Time.Delta );

		var position = ray.Position + ray.Forward * _distance - physics.Rotation * GrabPoint;
		if ( GridSize > 0 && float.IsFinite( GridSize ) ) position = position.SnapToGrid( System.MathF.Min( GridSize, 4096 ) );
		var rotation = HoldRotation( yaw );
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

	SceneTraceResult TraceTarget( Ray ray ) => Scene.Trace.Ray( ray, MaxRange * 2 )
		.IgnoreGameObjectHierarchy( GameObject.Root ).WithoutTags( "trigger", "projectile" ).Run();

	internal static bool CanMove( Rigidbody body, Connection grabber )
	{
		if ( !body.IsValid() || body.IsProxy || body.GetComponentInParent<PlayerController>().IsValid() ) return false;
		var grab = new IPhysgunEvent.GrabEvent { Grabber = grabber };
		body.GameObject.Root.RunEvent<IPhysgunEvent>( x => x.OnPhysgunGrab( grab ) );
		return !grab.Cancelled && body.IsValid();
	}

	void Release()
	{
		var body = HeldBody;
		var previousMass = _previousMassOverride;
		_previousMassOverride = null;
		HeldBody = null;
		BeamOn = false;
		if ( body.IsValid() ) body.GameObject.Root.RunEvent<IPhysgunEvent>( x => x.OnPhysgunDrop() );
		if ( previousMass.HasValue && body.IsValid() ) body.MassOverride = previousMass.Value;
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
