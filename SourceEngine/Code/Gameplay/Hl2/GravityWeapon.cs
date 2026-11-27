using System;

namespace Source1.Hl2;

/// <summary>Pulls and carries physics objects with secondary fire and punts them with primary fire.</summary>
[Title( "HL2 Gravity Weapon" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2GravityWeapon", "Hl2GravityWeapon" )]
public sealed class GravityWeapon : Weapon
{
	public GravityWeapon()
	{
		// Not a gun: what it throws does the damage.
		AttackDamageTags = [];
	}

	internal const string EnableMotionTag = "enable_on_physcannon";

	public enum PuntResult { Miss, Hit, HitDamageable, LaunchHeld }
	public enum CarryResult { Pickup, Drop, ForcedDrop }

	const float TraceLength = 250;
	const float PullLength = TraceLength * 4;
	const float PullForce = 4000;
	const float MaxMass = 250;
	const float MinLaunchSpeed = 700;
	const float MaxLaunchSpeed = 1500;
	[Sync( SyncFlags.FromHost )] public GameObject HeldObject { get; private set; }
	[Sync] public bool Pulling { get; set; }
	const float MaxCarrySpeed = 2500;
	const float MaxCarrySpin = 20 * MathF.PI; // 3600 degrees per second
	readonly List<(Rigidbody Body, float MassOverride, float AngularDamping)> _carriedBodies = [];
	PhysicsOwnership _borrowed;
	Rigidbody _pendingPickup;
	float _loadWeight;
	Vector3 _carryTarget;
	Vector3 _carryCenterOffset;
	Rotation _holdOffset;
	float? _launchSpin;
	float _carryError;
	float _carryTurnRate;
	TimeUntil _carryGrace;
	// Launching a held object consumes the current secondary press.
	bool _attack2Debounce;
	PhysCannonEffects _effects;
	bool _playedDenySound;

	protected override void OnStart()
	{
		base.OnStart();
		_ = WeaponSetup.ConfigureWeapon( this );
	}

	protected override void OnControl()
	{
		base.OnControl();
		if ( !Input.Down( "attack2" ) ) _attack2Debounce = false;
		Pulling = Input.Down( "attack2" ) && !_attack2Debounce && !HeldObject.IsValid();
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		Punt();
	}

	public override bool CanSecondaryAttack() => HeldObject.IsValid() && base.CanSecondaryAttack();

	public override void SecondaryAttack()
	{
		if ( !HeldObject.IsValid() ) return;
		SetNextFire( 0.5f );
		Drop();
	}

	SceneTrace TargetTrace( Ray ray, float range ) => Scene.Trace.Ray( ray, range )
		.IgnoreGameObjectHierarchy( GameObject.Root ).WithoutTags( "trigger" );

	static bool HitEntity( SceneTraceResult hit ) => hit.Hit && hit.GameObject.IsValid()
		&& (!hit.HasTag( "world" ) || hit.GameObject.Tags.Has( "source1_entity" ));

	SceneTraceResult FindTarget( float range )
	{
		var ray = TargetTrace( AimRay, range ).WithoutTags( "player" );
		var hit = ray.Run();
		return HitEntity( hit ) ? hit : ray.Size( 8 ).Run();
	}

	Rigidbody FindBodyInCone( Ray aim )
	{
		var nearest = TraceLength + 1;
		Rigidbody result = null;
		foreach ( var obj in Scene.FindInPhysics( new Sphere( aim.Position, nearest ) ) )
		{
			var body = obj.GetComponentInParent<Rigidbody>();
			if ( !body.IsValid() ) continue;
			var offset = body.GameObject.GetBounds().Center - aim.Position;
			var distance = offset.Length;
			if ( distance >= nearest || offset.Dot( aim.Forward ) <= distance * 0.97f ) continue;
			var trace = TargetTrace( new Ray( aim.Position, offset / distance ), distance ).WithoutTags( "player" ).Run();
			if ( trace.GameObject?.GetComponentInParent<Rigidbody>() != body ) continue;
			nearest = distance;
			result = body;
		}
		return result;
	}

	static bool CanMove( Rigidbody body ) => body.IsValid() && body.MotionEnabled && !body.IsProxy && body.PhysicsBody.IsValid();

	bool CanPickUp( Rigidbody body ) => body.IsValid() && body.PhysicsBody.IsValid() && Math.Max( body.Mass, body.MassOverride ) <= MaxMass
		&& (body.MotionEnabled || body.Tags.Has( EnableMotionTag )) && !IsStandingOn( body )
		&& !IsFlesh( body ) && !AlreadyHeld( body.GameObject );

	bool AlreadyHeld( GameObject target )
	{
		var entity = CarryEntity( target );
		foreach ( var gun in Scene.GetAllComponents<GravityWeapon>() )
		{
			if ( gun.HeldObject.IsValid() && CarryEntity( gun.HeldObject ) == entity ) return true;
		}
		return false;
	}

	// A model with several physics bodies is still one pickup.
	static GameObject CarryEntity( GameObject part ) => part.GetComponentInParent<ModelPhysics>()?.GameObject ?? part;

	bool IsStandingOn( Rigidbody body ) => Owner.GroundObject?.GetComponentInParent<Rigidbody>() == body;

	static bool IsFlesh( Rigidbody body )
	{
		if ( HasFleshSurface( body ) ) return true;
		if ( body.GetComponentInParent<ModelPhysics>() is not { } model ) return false;
		foreach ( var part in model.Bodies )
			if ( part.Component != body && HasFleshSurface( part.Component ) ) return true;
		return false;

		static bool HasFleshSurface( Rigidbody part ) => part?.PhysicsBody is { IsValid: true } physics &&
			(physics.Surface?.HasTag( "flesh" ) == true || physics.Shapes.Any( shape => shape.Surface?.HasTag( "flesh" ) == true ));
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Punt()
	{
		var forward = AimRay.Forward;
		if ( HeldObject.IsValid() )
		{
			var playerCenter = Owner.WorldPosition + Vector3.Up * (Owner.CurrentHeight * 0.5f);
			var center = HeldObject.GetBounds().Center;
			if ( playerCenter.Distance( center ) > TraceLength )
			{
				PuntEffects( PuntResult.Miss );
				return;
			}
			var held = HeldObject.GetComponent<Rigidbody>();
			Release( launching: true );
			if ( !held.IsValid() ) return;
			var model = held.GetComponentInParent<ModelPhysics>();
			var mass = model is { Bodies.Count: > 0 } ? model.Bodies[0].Component.Mass : held.Mass;
			var t = Math.Clamp( (mass - 100) / 500, 0, 1 );
			var velocity = forward * float.Lerp( MaxLaunchSpeed, MinLaunchSpeed, t * t * (3 - 2 * t) );
			var spin = _launchSpin is { } launchSpin
				? Vector3.Up * launchSpin
				: new Vector3( Game.Random.Float( -600, 600 ), Game.Random.Float( -600, 600 ), Game.Random.Float( -600, 600 ) );
			spin *= MathF.PI / 180;
			if ( model is { Bodies.Count: > 1 } )
			{
				foreach ( var part in model.Bodies )
					if ( CanMove( part.Component ) ) Launch( part.Component );
			}
			else Launch( held );
			PlayerThrows.Mark( held.GameObject, Owner?.GameObject );
			RestoreOwner();

			void Launch( Rigidbody body )
			{
				body.Velocity = velocity;
				body.AngularVelocity = body.WorldRotation * spin;
			}
			PuntEffects( PuntResult.LaunchHeld, center );
			return;
		}
		var ray = TargetTrace( AimRay, TraceLength );
		// A hull can graze the world beside an otherwise reachable target.
		var trace = ray.Size( 16 ).Run();
		var result = PuntTarget( trace, forward );
		if ( result is null )
		{
			trace = ray.Run();
			result = PuntTarget( trace, forward );
		}
		PuntEffects( result ?? PuntResult.Miss, trace.EndPosition );
	}

	PuntResult? PuntTarget( SceneTraceResult trace, Vector3 forward )
	{
		var obj = trace.GameObject;
		if ( !obj.IsValid() ) return null;
		if ( obj.GetComponentInParent<PlayerController>() is not null ) return PuntResult.Miss;

		if ( obj.GetComponentInParent<Rigidbody>() is { } body )
		{
			if ( !body.PhysicsBody.IsValid() || IsFlesh( body ) ) return PuntResult.Miss;
			if ( !body.MotionEnabled && !body.Tags.Has( EnableMotionTag ) ) return PuntResult.Hit;
			if ( forward.z < 0 ) forward = forward.WithZ( forward.z * -0.65f );
			if ( body.IsProxy )
			{
				using var recipients = Rpc.FilterInclude( body.Network.Owner );
				PuntRemoteBody( body, forward, trace.EndPosition );
			}
			else
			{
				PuntBodies( body, forward, trace.EndPosition );
				PlayerThrows.Mark( body.GameObject, Owner?.GameObject );
			}
			return PuntResult.Hit;
		}

		var target = obj.GetComponentInParent<IDamageable>();
		if ( target is null or Prop { Health: <= 0 } ) return null;

		var damage = new DamageInfo( 1, Owner.GameObject, GameObject, trace.Hitbox )
		{
			Origin = AimRay.Position,
			Position = trace.EndPosition,
			Normal = trace.Normal,
			Shape = trace.Shape
		};
		damage.Tags.Add( DamageTags.Crush );
		damage.Tags.Add( "physgun" );
		target.OnDamage( damage );
		return PuntResult.HitDamageable;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PuntRemoteBody( Rigidbody body, Vector3 forward, Vector3 hit )
	{
		if ( body.IsValid() && !body.IsProxy ) PuntBodies( body, forward, hit );
	}

	static void PuntBodies( Rigidbody struck, Vector3 forward, Vector3 hit )
	{
		if ( !struck.MotionEnabled ) struck.MotionEnabled = true;
		var entity = CarryEntity( struck.GameObject );
		var massLimit = entity.GetComponent<Sandbox.Movement.ISitTarget>() is null ? MaxMass : MaxMass * 2.5f;
		if ( entity.GetComponent<ModelPhysics>() is not { Bodies.Count: > 1 } model )
		{
			Apply( struck, struck.Mass, 1 );
			return;
		}

		var mass = model.Mass;
		for ( int i = 0; i < model.Bodies.Count; ++i )
		{
			var part = model.Bodies[i].Component;
			if ( !CanMove( part ) ) continue;
			var share = part.Mass / mass;
			// Source weights the root body even when the trace strikes a limb.
			Apply( part, mass, i == 0 ? MathF.Min( 1, share + 0.5f ) : share * 0.5f );
		}

		void Apply( Rigidbody part, float totalMass, float share )
		{
			part.ApplyImpulse( forward * (15000 * share) );
			part.ApplyImpulseAt( hit, forward * (MathF.Min( totalMass, massLimit ) * 600 * share) );
			part.Velocity = part.Velocity.ClampLength( 4000 );
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Drop()
	{
		if ( !HeldObject.IsValid() ) return;
		Release();
		CarryEffects( CarryResult.Drop );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void CarryEffects( CarryResult result )
	{
		if ( result == CarryResult.Pickup ) SetNextSecondaryFire( 0.5f );
		if ( result == CarryResult.Drop ) SetNextFire( 0.5f );
		if ( result != CarryResult.ForcedDrop ) (WeaponModel as ViewModel)?.PlayPhyscannonAttack();
		Sounds.PlayWeaponSound( this, result == CarryResult.Pickup ? "special1" : "melee_miss" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void DenyPickup() => Sounds.PlayWeaponSound( this, "special3" );

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	internal void PlayClawSound( bool open ) => Sounds.PlayWeaponSound( this, open ? "special2" : "melee_hit" );

	internal override bool ShouldPlayIdle => !Input.Down( "attack1" );

	void PuntEffects( PuntResult result, Vector3 impact = default ) => PlayPuntEffects( result, impact, Game.Random.Next() );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayPuntEffects( PuntResult result, Vector3 impact, int soundSeed )
	{
		var launch = result != PuntResult.Miss;
		Sounds.PlayWeaponSound( this, launch ? "single_shot" : "empty", soundSeed );
		(WeaponModel as ViewModel)?.PlayPhyscannonAttack( launch );
		if ( !launch ) return;

		if ( Owner is { IsProxy: false, ThirdPerson: false } && !Application.IsDedicatedServer )
		{
			var punch = new Angles( -6, Game.Random.Next( -2, 3 ), 0 );
			if ( result == PuntResult.Hit )
				punch += new Angles( Game.Random.Float( 1, 2 ), Game.Random.Float( -1, 1 ), 0 );
			ViewPunch.Apply( Owner, punch, reset: false );
		}
		if ( result == PuntResult.LaunchHeld ) _attack2Debounce = true;
		SetNextSecondaryFire( PrimaryDelay );
		_effects?.Launch( this, result == PuntResult.LaunchHeld, impact );
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !Networking.IsHost ) return;
		if ( !Pulling || !Active ) _playedDenySound = false;
		if ( HeldObject.IsValid() )
		{
			Hold();
			return;
		}
		if ( _carriedBodies.Count > 0 ) Release();
		if ( _pendingPickup is not null && (!Pulling || !Active) ) RestoreOwner();
		// A 0.1f retry rounds just past the fifth 50 Hz physics tick.
		if ( Pulling && Active && (_pendingPickup is not null || NextSecondaryFire <= 0.000001f) ) Pull();
	}

	void Pull()
	{
		SetNextSecondaryFire( 0.1f );
		var aim = AimRay;
		var hit = FindTarget( PullLength );
		var direct = HitEntity( hit );
		var body = direct ? hit.GameObject.GetComponentInParent<Rigidbody>() : FindBodyInCone( aim );
		if ( _pendingPickup is not null && body != _pendingPickup ) RestoreOwner();

		if ( !CanPickUp( body ) )
		{
			RestoreOwner();
			if ( _playedDenySound ) return;
			_playedDenySound = true;
			DenyPickup();
			return;
		}
		var target = direct ? hit.HitPosition : body.GameObject.GetBounds().Center;
		if ( aim.Position.Distance( target ) > TraceLength )
		{
			RestoreOwner();
			if ( !body.MotionEnabled ) return;
			var direction = (aim.Position - body.GameObject.GetBounds().Center).Normal;
			if ( body.IsProxy )
			{
				using var recipients = Rpc.FilterInclude( body.Network.Owner );
				PullRemoteBody( body, direction );
			}
			else PullBody( body, direction );
			return;
		}
		if ( _pendingPickup is null )
		{
			bool remote = body.IsProxy;
			if ( body.GetComponentInParent<PlayerController>().IsValid()
				|| !PhysicsOwnership.TryBorrow( body, CarryEntity( body.GameObject ), out _borrowed ) ) return;
			if ( remote )
			{
				// The new physics owner gets the simulated mass on the next physics step.
				_pendingPickup = body;
				return;
			}
		}
		BeginCarry( body );
	}

	void BeginCarry( Rigidbody body )
	{
		_pendingPickup = null;
		if ( !body.MotionEnabled ) body.MotionEnabled = true;
		HeldObject = body.GameObject;
		ApplyCarryPhysics( body );
		var model = body.GetComponent<ModelRenderer>()?.Model ?? body.GetComponent<ModelCollider>()?.Model;
		// Ragdolls are grabbed at the selected physics body's origin.
		_carryCenterOffset = body.GetComponentInParent<ModelPhysics>() is not null ? Vector3.Zero
			: model is not null ? model.Bounds.Center * body.WorldScale
			: body.WorldRotation.Inverse * (body.PhysicsBody.GetBounds().Center - body.PhysicsBody.Position);
		var interactions = model?.GetData<PhysGunInteractions>();
		_holdOffset = interactions?.PreferredCarryAngles is { } angles ? Rotation.From( angles ) : Owner.EyeTransform.Rotation.Inverse * body.WorldRotation;
		_launchSpin = interactions?.LaunchSpin;
		_carryTarget = body.PhysicsBody.Position;
		_carryGrace = 1;
		_carryError = 0;
		_carryTurnRate = 0;
		foreach ( var npc in HeldObject.Root.GetComponentsInChildren<Sandbox.Npcs.BaseNpc>() )
			npc.StartCarry( GameObject );
		CarryEffects( CarryResult.Pickup );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PullRemoteBody( Rigidbody body, Vector3 direction )
	{
		if ( body.IsValid() && !body.IsProxy ) PullBody( body, direction );
	}

	static void PullBody( Rigidbody body, Vector3 direction )
	{
		var mass = body.Mass;
		if ( !body.MotionEnabled || mass > MaxMass ) return;
		var scale = mass < 50 ? (mass + 0.5f) / 50 : 1;
		body.ApplyImpulse( direction * (PullForce * scale) );
	}

	void ApplyCarryPhysics( Rigidbody grabbed )
	{
		if ( grabbed.GetComponentInParent<ModelPhysics>() is { Bodies.Count: > 1 } model )
		{
			var mass = MathF.Min( 1, 7.5f / model.Bodies.Count );
			foreach ( var part in model.Bodies )
				Apply( part.Component, part.Component == grabbed ? 1 : mass );
		}
		else Apply( grabbed, 1 );

		void Apply( Rigidbody body, float mass )
		{
			_loadWeight += body.Mass;
			_carriedBodies.Add( (body, body.MassOverride, body.AngularDamping) );
			body.MassOverride = mass;
			// VPhysics applies rotational damping through its gravity controller.
			body.AngularDamping = body.PhysicsBody.GravityEnabled ? 10 : 0;
		}
	}

	bool CanContinueCarry( Rigidbody body )
	{
		if ( !CanMove( body ) || IsStandingOn( body ) ) return false;
		if ( _carryGrace > 0 ) return true;

		var error = body.PhysicsBody.Position.Distance( _carryTarget );
		// The grab controller discounts errors beyond its 2500 units/s tracking speed.
		if ( error > MaxCarrySpeed * Time.Delta ) error *= 0.5f;
		_carryError = float.Lerp( _carryError, error, MathF.Min( Time.Delta, 1 ) );
		return _carryError <= 12;
	}

	void Hold()
	{
		var body = HeldObject.GetComponent<Rigidbody>();
		if ( !Active || !CanContinueCarry( body ) )
		{
			Release();
			if ( Active ) CarryEffects( CarryResult.ForcedDrop );
			return;
		}
		var eye = Owner.EyeTransform;
		var angles = eye.Rotation.Angles();
		var forward = angles.WithPitch( Math.Clamp( angles.pitch, -75, 75 ) ).Forward;
		var nearFace = body.PhysicsBody.FindClosestPoint( body.WorldPosition - forward * PullLength );
		var extent = MathF.Abs( forward.Dot( nearFace - body.WorldPosition ) );
		var radius = Owner.BodyRadius * MathF.Sqrt( 2 ) + extent;
		var wall = Scene.Trace.Ray( eye.Position, eye.Position + forward * (24 + 2 * radius) )
			.WithTag( "world" ).IgnoreGameObjectHierarchy( HeldObject ).Run();
		var target = eye.Position + forward * (wall.Fraction < 0.5f ? radius * 0.5f : 24 + radius);

		// Keep the prop outside the player's vertical axis even when looking straight down.
		var axis = Owner.WorldPosition.WithZ( target.z );
		var offset = target - axis;
		if ( offset.LengthSquared < radius * radius ) target = axis + offset.Normal * radius;
		var orientation = eye.Rotation * _holdOffset;
		_carryTarget = target - orientation * _carryCenterOffset;
		MoveCarriedBody( body, _carryTarget, orientation );
	}

	void MoveCarriedBody( Rigidbody body, Vector3 target, Rotation orientation )
	{
		var velocity = ShadowController.Velocity( body.Velocity, target - body.PhysicsBody.Position, MaxCarrySpeed, MaxCarrySpeed * 2 );
		var contact = Scene.Trace.Body( body, body.PhysicsBody.Position + velocity * Time.Delta )
			.IgnoreGameObjectHierarchy( HeldObject ).WithoutTags( "trigger" ).Run();
		var other = contact.GameObject?.GetComponentInParent<Rigidbody>();
		bool obstructed = contact.Hit && (other is not { MotionEnabled: true } || other.Mass > _loadWeight);
		if ( obstructed )
		{
			var normal = contact.Normal;
			if ( contact.StartedSolid && contact.Body.IsValid() )
			{
				// An overlapping sweep can report the reverse sweep direction, even when separating.
				var center = body.PhysicsBody.MassCenter;
				normal = (center - contact.Body.FindClosestPoint( center )).Normal;
			}
			velocity -= normal * (MathF.Min( velocity.Dot( normal ), 0 ) * (1 - contact.Fraction));
		}
		body.Velocity = velocity;

		_carryTurnRate = _carryTurnRate.Approach( obstructed ? 0.1f : 1, 2 * Time.Delta );
		var angularError = ShadowController.AngularError( body.WorldRotation, orientation );
		var spinLimit = MaxCarrySpin * _carryTurnRate * _carryTurnRate * _carryTurnRate;
		body.AngularVelocity = ShadowController.Velocity( body.AngularVelocity, angularError, spinLimit, MaxCarrySpin * 2 );
	}

	void RestoreOwner()
	{
		_pendingPickup = null;
		_borrowed?.Release();
		_borrowed = null;
	}

	void Release( bool launching = false )
	{
		var held = HeldObject;
		HeldObject = null;
		foreach ( var (part, massOverride, angularDamping) in _carriedBodies )
		{
			if ( !part.IsValid() ) continue;
			part.MassOverride = massOverride;
			part.AngularDamping = angularDamping;
		}
		_carriedBodies.Clear();
		_loadWeight = 0;
		if ( !held.IsValid() )
		{
			RestoreOwner();
			return;
		}
		if ( held.GetComponent<Rigidbody>() is { } body && CanMove( body ) )
		{
			body.Velocity = body.Velocity.ClampLength( 500 );
			body.AngularVelocity = body.AngularVelocity.ClampLength( 4 * MathF.PI );
		}
		foreach ( var npc in held.Root.GetComponentsInChildren<Sandbox.Npcs.BaseNpc>() )
		{
			npc.StopCarry( GameObject );
		}
		if ( !launching ) RestoreOwner();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Application.IsDedicatedServer ) return;
		_effects ??= new PhysCannonEffects();
		// Only the owner traces; other peers show the idle claws.
		var ready = !IsProxy && !HeldObject.IsValid() && IsHeld && CanPickUp( FindTarget( TraceLength ).GameObject?.GetComponentInParent<Rigidbody>() );
		_effects.Update( this, HeldObject, ready );
	}

	protected override void OnViewModelPlaced( ViewModel model ) => _effects?.Follow();

	public override void DrawCrosshair( Painter painter, Vector2 center ) => Crosshair.Draw( painter, center, CanPrimaryAttack(), 0 );

	protected override bool OnHolstering( BaseInventoryItem next ) => !HeldObject.IsValid() && base.OnHolstering( next );

	protected override void OnHolstered()
	{
		if ( Networking.IsHost ) Release();
		base.OnHolstered();
	}

	protected override void OnDisabled()
	{
		if ( Networking.IsHost ) Release();
		_effects?.Dispose();
		_effects = null;
		base.OnDisabled();
	}

}
