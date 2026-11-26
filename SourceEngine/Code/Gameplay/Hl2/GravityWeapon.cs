using System;

namespace Source1.Hl2;

/// <summary>
/// weapon_physcannon: punts with attack1, pulls and carries with attack2. Values follow the SDK
/// (physcannon_tracelength 250, punt impulse per mass, 250 kg carry limit); the body is held with
/// velocity control on the host, which simulates it. An NPC it carries is told through
/// <see cref="Sandbox.Npcs.BaseNpc.StartCarry"/> and <see cref="Sandbox.Npcs.BaseNpc.StopCarry"/>.
/// </summary>
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

	enum PuntResult { Miss, Hit, HitDamageable, LaunchHeld }
	enum CarryResult { Pickup, Drop, ForcedDrop }

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
		.IgnoreGameObjectHierarchy( GameObject.Root ).WithoutTags( "trigger", "projectile" );

	Rigidbody FindBody( float range, out Vector3 hitPosition )
	{
		var ray = TargetTrace( AimRay, range ).WithoutTags( "player" );
		var tr = ray.Run();
		var body = tr.GameObject?.GetComponentInParent<Rigidbody>();
		if ( !body.IsValid() )
		{
			tr = ray.Size( 8 ).Run();
			body = tr.GameObject?.GetComponentInParent<Rigidbody>();
		}
		hitPosition = tr.HitPosition;
		return body;
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

	bool CanPickUp( Rigidbody body ) => body.IsValid() && body.PhysicsBody.IsValid() && body.Mass <= MaxMass
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
		if ( body.GetComponentInParent<ModelPhysics>() is { } model )
		{
			foreach ( var part in model.Bodies )
				if ( part.Component != body && HasFleshSurface( part.Component ) ) return true;
		}
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
			Release();
			if ( !held.IsValid() ) return;
			// Pickup_DefaultPhysGunLaunchVelocity: physcannon_maxforce up to 100 kg, splined down to physcannon_minforce at 600.
			var t = Math.Clamp( (held.Mass - 100) / 500, 0, 1 );
			held.Velocity = forward * float.Lerp( MaxLaunchSpeed, MinLaunchSpeed, t * t * (3 - 2 * t) );
			var spin = _launchSpin is { } launchSpin
				? Vector3.Up * launchSpin
				: new Vector3( Game.Random.Float( -600, 600 ), Game.Random.Float( -600, 600 ), Game.Random.Float( -600, 600 ) );
			held.AngularVelocity = held.WorldRotation * spin * (MathF.PI / 180);
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
			if ( body.IsProxy || !body.PhysicsBody.IsValid() || IsFlesh( body ) ) return PuntResult.Miss;
			if ( !body.MotionEnabled )
			{
				if ( !body.Tags.Has( EnableMotionTag ) ) return PuntResult.Hit;
				body.MotionEnabled = true;
			}
			if ( forward.z < 0 ) forward = forward.WithZ( forward.z * -0.65f );
			PuntBodies( body, forward, trace.EndPosition );
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

	static void PuntBodies( Rigidbody struck, Vector3 forward, Vector3 hit )
	{
		if ( struck.GetComponentInParent<ModelPhysics>() is not { Bodies.Count: > 1 } model )
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
			part.ApplyImpulseAt( hit, forward * (MathF.Min( totalMass, MaxMass ) * 600 * share) );
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

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PuntEffects( PuntResult result, Vector3 impact = default )
	{
		var launch = result != PuntResult.Miss;
		Sounds.PlayWeaponSound( this, launch ? "single_shot" : "empty" );
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
		_effects?.Launch( result == PuntResult.LaunchHeld, impact );
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
		if ( Pulling && Active && NextSecondaryFire <= 0 ) Pull();
	}

	void Pull()
	{
		SetNextSecondaryFire( 0.1f );
		var aim = AimRay;
		var body = FindBody( PullLength, out var hit );
		bool pull = body.IsValid() && aim.Position.Distance( hit ) > TraceLength;
		body ??= FindBodyInCone( aim );
		if ( !CanPickUp( body ) || body.IsProxy )
		{
			if ( _playedDenySound ) return;
			_playedDenySound = true;
			DenyPickup();
			return;
		}
		if ( pull )
		{
			if ( !body.MotionEnabled ) return;
			var direction = (aim.Position - body.GameObject.GetBounds().Center).Normal;
			var scale = body.Mass < 50 ? (body.Mass + 0.5f) / 50 : 1;
			body.ApplyImpulse( direction * (PullForce * scale) );
			return;
		}
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

	void Release()
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
		if ( !held.IsValid() ) return;
		if ( held.GetComponent<Rigidbody>() is { } body && CanMove( body ) )
		{
			body.Velocity = body.Velocity.ClampLength( 500 );
			body.AngularVelocity = body.AngularVelocity.ClampLength( 4 * MathF.PI );
		}
		foreach ( var npc in held.Root.GetComponentsInChildren<Sandbox.Npcs.BaseNpc>() )
		{
			npc.StopCarry( GameObject );
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Application.IsDedicatedServer ) return;
		_effects ??= new PhysCannonEffects();
		// Only the owner traces; other peers show the idle claws.
		var ready = !IsProxy && !HeldObject.IsValid() && IsHeld && CanPickUp( FindBody( TraceLength, out _ ) );
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

/// <summary>
/// The physcannon's code-driven sprites and beams, reconstructed from the SDK's StartEffects and
/// DoEffect states: a core glow at the first attachment, glows on every prong, endcaps and beams
/// from the prong tips to the core while carrying.
/// </summary>
sealed class PhysCannonEffects : IDisposable
{
	static readonly string[] Prongs = ["fork1b", "fork1m", "fork1t", "fork2b", "fork2m", "fork2t"];
	static readonly string[] ViewTips = ["fork1t", "fork2t"];
	static readonly string[] WorldTips = ["fork1t", "fork2t", "fork3t"];
	static readonly Curve ViewBeamWidth = BeamWidth( 0, 4 );
	static readonly Curve WorldBeamWidth = BeamWidth( 0.5f, 2 );
	static Curve BeamWidth( float start, float end ) => new( new Curve.Frame( 0, start ) { Mode = Curve.HandleMode.Linear }, new Curve.Frame( 1, end ) );
	// StartEffects tints the prong glows kRenderTransAdd 255, 128, 0.
	static readonly Color GlowColor = new( 1, 0.5f, 0 );
	SkinnedModelRenderer _renderer;
	ViewModel _view;
	GameObject _root;
	GameObject _coreAnchor;
	LineRenderer _puntBeam;
	Vector3 _impact;
	SpriteRenderer _core;
	SpriteRenderer _blast;
	readonly List<SpriteRenderer> _glows = new();
	readonly List<SpriteRenderer> _endcaps = new();
	readonly List<(GameObject Tip, LineRenderer Beam)> _beams = new();
	readonly Dictionary<SpriteRenderer, GameObject> _anchors = new();
	TimeSince _launch = 10;
	TimeUntil _changeAt;
	bool _closePending;
	bool _open;
	float _elementFrom;
	TimeSince _elementMoved = 10;
	SoundHandle _holdSound;
	Vector2 _coreFrom, _coreTarget;
	TimeSince _coreChanged;
	bool _motorHolding;
	TimeSince _motorChanged;
	float _releaseVolume;

	public void Launch( bool fromHold, Vector3 impact )
	{
		_impact = impact;
		_launch = 0;
		_changeAt = fromHold ? 0.1f : 0.5f;
		_closePending = true;
	}

	public void Update( GravityWeapon weapon, GameObject held, bool ready )
	{
		var model = weapon.WeaponModel;
		var renderer = model.IsValid() ? model.Renderer : null;
		if ( renderer != _renderer )
		{
			Dispose();
			_renderer = renderer;
		}
		if ( !renderer.IsValid() || !renderer.Model.IsValid() || !renderer.Enabled ) return;
		var mount = MountContent.Of( renderer.Model );
		if ( !_root.IsValid() ) Build( renderer, model as ViewModel, mount, weapon.Owner is { IsProxy: false } );
		var holding = held.IsValid();
		UpdateElements( weapon, holding, ready );
		UpdateMotor( weapon, holding, mount );
		UpdateCore( holding, weapon.IsHeld && weapon.Active );
		float launchAge = _launch;
		SetGlow( _blast, float.Lerp( 8, 64, Math.Min( 1, launchAge / 0.1f ) ), 255 * Math.Max( 0, 1 - launchAge / 0.2f ) );
		foreach ( var glow in _glows )
		{
			if ( holding ) MountedEffects.SetSprite( glow, 0.5f, 64, GlowColor );
			else if ( ready ) MountedEffects.SetSprite( glow, 0.4f, 32, GlowColor );
			else MountedEffects.SetSprite( glow, Game.Random.Float( 0.3f, 0.35f ), Game.Random.Float( 16, 24 ), GlowColor );
		}
		foreach ( var endcap in _endcaps )
			MountedEffects.SetSprite( endcap, holding ? Game.Random.Float( 0.1f, 0.15f ) : 0, Game.Random.Float( 200, 255 ) );
		foreach ( var (tip, beam) in _beams )
		{
			beam.Enabled = tip.IsValid() && holding;
		}
		if ( _puntBeam.IsValid() ) _puntBeam.Enabled = launchAge < 0.1f;
		Follow();
	}

	/// <summary>
	/// Moves the effects onto the attachments. The weapon also calls this once the viewmodel is placed for the frame,
	/// which happens after updates, so they don't trail the view.
	/// </summary>
	public void Follow()
	{
		foreach ( var (sprite, anchor) in _anchors )
		{
			if ( !sprite.IsValid() || !anchor.IsValid() ) continue;
			if ( _view.IsValid() ) _view.PlaceBehind( sprite.GameObject, Place( anchor ) );
			else sprite.WorldPosition = Place( anchor );
		}
		foreach ( var (tip, beam) in _beams )
		{
			if ( !tip.IsValid() || !beam.IsValid() || !beam.Enabled ) continue;
			beam.VectorPoints[0] = Place( tip );
			beam.VectorPoints[1] = Place( _coreAnchor );
		}
		if ( _puntBeam.IsValid() && _puntBeam.Enabled )
		{
			_puntBeam.VectorPoints[0] = Place( _coreAnchor );
			_puntBeam.VectorPoints[1] = _impact;
		}
	}

	/// <summary>
	/// OpenElements and CloseElements: the claws spline between closed and open over 0.2s
	/// through the viewmodel's "active" pose parameter.
	/// </summary>
	void UpdateElements( GravityWeapon weapon, bool holding, bool ready )
	{
		var open = _open;
		if ( holding || (ready && _launch >= 0.25f) )
		{
			_closePending = false;
			open = true;
		}
		else if ( _changeAt <= 0 )
		{
			if ( _closePending ) open = false;
			else _changeAt = 0.5f;
			_closePending = !_closePending;
		}

		var current = Element();
		if ( open != _open )
		{
			_elementFrom = current;
			_elementMoved = 0;
			_open = open;
			_view?.OnClawsChanged();
			if ( !weapon.IsProxy && weapon.IsHeld ) weapon.PlayClawSound( open );
			current = Element();
		}
		_view?.SetPoseParameter( "active", current );
	}

	void UpdateCore( bool holding, bool equipped )
	{
		if ( !_core.IsValid() ) return;
		var firstPerson = _view.IsValid();
		var target = holding ? new Vector2( firstPerson ? 13 : 14, 255 )
			: equipped ? new Vector2( firstPerson ? 10 : 8, firstPerson ? 128 : 0 ) : Vector2.Zero;
		if ( target != _coreTarget )
		{
			_coreFrom = _core.Size.x > 0 ? new Vector2( _core.Size.x, _core.Color.r * 255 ) : Vector2.Zero;
			_coreTarget = target;
			_coreChanged = 0;
		}
		var width = float.Lerp( _coreFrom.x, target.x, Math.Min( 1, _coreChanged * 5 ) );
		var brightness = float.Lerp( _coreFrom.y, target.y, Math.Min( 1, _coreChanged / (holding ? 0.1f : 0.2f) ) );
		SetGlow( _core, width, brightness );
	}

	static void SetGlow( SpriteRenderer sprite, float width, float brightness )
	{
		if ( !sprite.IsValid() ) return;
		sprite.Size = width;
		sprite.Color = (Color.White * (brightness / 255f)).WithAlpha( 1 );
		sprite.Enabled = brightness > 0;
	}

	void UpdateMotor( GravityWeapon weapon, bool holding, string mount )
	{
		if ( holding != _motorHolding )
		{
			_motorHolding = holding;
			_motorChanged = 0;
			if ( holding && _holdSound is null && Sounds.Sound( "Weapon_PhysCannon.HoldSound", mount ) is { } sound )
				_holdSound = weapon.GameObject.PlaySound( sound );
			else if ( !holding ) _releaseVolume = _holdSound?.Volume ?? 0;
		}
		if ( _holdSound is null ) return;
		var volume = holding ? 0.8f * Math.Min( 1, _motorChanged * 2 ) : _releaseVolume * Math.Max( 0, 1 - _motorChanged );
		_holdSound.Volume = volume;
		_holdSound.Pitch = float.Lerp( 0.5f, 1, volume / 0.8f );
		if ( !holding && volume == 0 )
		{
			_holdSound.Stop();
			_holdSound = null;
		}
	}

	float Element()
	{
		var t = Math.Clamp( _elementMoved / (_open ? 0.2f : 0.5f), 0, 1 );
		var spline = t * t * (3 - 2 * t);
		return float.Lerp( _elementFrom, _open ? 1 : 0, spline );
	}

	Vector3 Place( GameObject anchor ) => _view.IsValid() ? _view.DrawnPosition( anchor ) : anchor.WorldPosition;

	SpriteRenderer AddSprite( GameObject anchor, string name, string mount )
	{
		var sprite = MountedEffects.AddSprite( _root, name, mount );
		if ( sprite.IsValid() ) _anchors[sprite] = anchor;
		return sprite;
	}

	void Build( SkinnedModelRenderer renderer, ViewModel view, string mount, bool localPlayer )
	{
		_view = view;
		var viewModel = view.IsValid();
		_root = new GameObject( true, "Physcannon effects" );
		_root.NetworkMode = NetworkMode.Never;
		var coreName = renderer.Model.Attachments.All.FirstOrDefault()?.Name;
		var core = coreName is null ? renderer.GameObject : renderer.GetAttachmentObject( coreName ) ?? renderer.GameObject;
		_coreAnchor = core;
		_core = AddSprite( core, "sprites/orangecore1", mount );
		_blast = AddSprite( core, "sprites/orangecore2", mount );
		if ( localPlayer && MountedEffects.AddBeam( _root, "sprites/orangelight1", mount ) is { } punt )
		{
			_puntBeam = punt;
			punt.Width = BeamWidth( 12, 4 );
			punt.Color = Color.White;
			punt.Enabled = false;
		}
		// sprites/glow04_noz names the glow04 texture.
		foreach ( var prong in Prongs.Select( renderer.GetAttachmentObject ).Where( x => x.IsValid() ) )
			if ( AddSprite( prong, "sprites/glow04", mount ) is { } glow ) _glows.Add( glow );
		foreach ( var name in viewModel ? ViewTips : WorldTips )
		{
			var tip = renderer.GetAttachmentObject( name ) ?? renderer.GameObject;
			if ( viewModel && AddSprite( tip, "sprites/orangeflare1", mount ) is { } endcap ) _endcaps.Add( endcap );
			if ( MountedEffects.AddBeam( _root, "sprites/orangelight1", mount ) is { } beam )
			{
				beam.Width = viewModel ? ViewBeamWidth : WorldBeamWidth;
				beam.Color = (Color.White * (128f / 255)).WithAlpha( 1 );
				_beams.Add( (tip, beam) );
			}
		}
	}

	public void Dispose()
	{
		_holdSound?.Stop( 0.2f );
		_holdSound = null;
		_motorHolding = false;
		_coreFrom = _coreTarget = Vector2.Zero;
		_root?.Destroy();
		_root = null;
		_puntBeam = null;
		_view = null;
		_glows.Clear();
		_endcaps.Clear();
		_beams.Clear();
		_anchors.Clear();
	}
}
