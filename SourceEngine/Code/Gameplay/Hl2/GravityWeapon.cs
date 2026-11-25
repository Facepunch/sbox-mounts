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

	enum PuntResult { Miss, Hit, LaunchHeld }
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
	Vector3 _carryTarget;
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
		.IgnoreGameObjectHierarchy( GameObject.Root ).WithoutTags( "trigger", "player", "projectile" );

	Rigidbody FindBody( float range, out Vector3 hitPosition, bool punt = false )
	{
		var ray = TargetTrace( AimRay, range );
		var tr = punt ? ray.Size( 16 ).Run() : ray.Run();
		var body = tr.GameObject?.GetComponentInParent<Rigidbody>();
		if ( !body.IsValid() || !body.MotionEnabled )
		{
			tr = punt ? ray.Run() : ray.Size( 8 ).Run();
			body = tr.GameObject?.GetComponentInParent<Rigidbody>();
		}
		hitPosition = punt ? tr.EndPosition : tr.HitPosition;
		return body.IsValid() && body.MotionEnabled ? body : null;
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
			var trace = TargetTrace( new Ray( aim.Position, offset / distance ), distance ).Run();
			if ( trace.GameObject?.GetComponentInParent<Rigidbody>() != body ) continue;
			nearest = distance;
			result = body;
		}
		return result;
	}

	static bool CanMove( Rigidbody body ) => body.IsValid() && body.MotionEnabled && !body.IsProxy && body.PhysicsBody.IsValid();

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
			if ( playerCenter.Distance( HeldObject.GetBounds().Center ) > TraceLength )
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
			PuntEffects( PuntResult.LaunchHeld );
			return;
		}
		var body = FindBody( TraceLength, out var hit, punt: true );
		if ( !CanMove( body ) || IsFlesh( body ) )
		{
			PuntEffects( PuntResult.Miss );
			return;
		}
		if ( forward.z < 0 ) forward = forward.WithZ( forward.z * -0.65f );
		PuntBodies( body, forward, hit );
		PuntEffects( PuntResult.Hit );
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
		if ( result != CarryResult.ForcedDrop ) (WeaponModel as ViewModel)?.PlayGravityGunAttack();
		if ( Sounds.WeaponSound( this, result == CarryResult.Pickup ? "special1" : "melee_miss" ) is { } sound ) GameObject.PlaySound( sound );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void DenyPickup()
	{
		if ( Sounds.WeaponSound( this, "special3" ) is { } sound ) GameObject.PlaySound( sound );
	}

	internal override bool ShouldPlayIdle => !Input.Down( "attack1" );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PuntEffects( PuntResult result )
	{
		var launch = result != PuntResult.Miss;
		if ( Sounds.WeaponSound( this, launch ? "single_shot" : "empty" ) is { } sound ) GameObject.PlaySound( sound );
		(WeaponModel as ViewModel)?.PlayGravityGunAttack( launch );
		if ( !launch ) return;

		if ( Owner is { IsProxy: false, ThirdPerson: false } && !Application.IsDedicatedServer )
		{
			var punch = new Angles( -6, Game.Random.Next( -2, 3 ), 0 );
			if ( result == PuntResult.Hit )
				punch += new Angles( Game.Random.Float( 1, 2 ), Game.Random.Float( -1, 1 ), 0 );
			ViewPunch.Apply( Scene.Camera, punch, reset: false );
		}
		if ( result == PuntResult.LaunchHeld ) _attack2Debounce = true;
		SetNextSecondaryFire( PrimaryDelay );
		_effects?.Launch( result == PuntResult.LaunchHeld );
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
		if ( Pulling && Active && NextSecondaryFire <= 0 ) Pull();
	}

	void Pull()
	{
		SetNextSecondaryFire( 0.1f );
		var aim = AimRay;
		var body = FindBody( PullLength, out var hit );
		bool pull = body.IsValid() && aim.Position.Distance( hit ) > TraceLength;
		body ??= FindBodyInCone( aim );
		if ( !CanMove( body ) || body.Mass > MaxMass || IsFlesh( body ) )
		{
			if ( _playedDenySound ) return;
			_playedDenySound = true;
			DenyPickup();
			return;
		}
		if ( pull )
		{
			var direction = (aim.Position - body.GameObject.GetBounds().Center).Normal;
			var scale = body.Mass < 50 ? (body.Mass + 0.5f) / 50 : 1;
			body.ApplyImpulse( direction * (PullForce * scale) );
			return;
		}
		HeldObject = body.GameObject;
		var interactions = body.GetComponent<ModelRenderer>()?.Model?.GetData<PhysGunInteractions>();
		_holdOffset = interactions?.PreferredCarryAngles is { } angles ? Rotation.From( angles ) : Owner.EyeTransform.Rotation.Inverse * body.WorldRotation;
		_launchSpin = interactions?.LaunchSpin;
		_carryTarget = body.PhysicsBody.MassCenter;
		_carryGrace = 1;
		_carryError = 0;
		_carryTurnRate = 0;
		foreach ( var npc in HeldObject.Root.GetComponentsInChildren<Sandbox.Npcs.BaseNpc>() )
			npc.StartCarry( GameObject );
		CarryEffects( CarryResult.Pickup );
	}

	bool CanContinueCarry( Rigidbody body )
	{
		if ( !CanMove( body ) || Owner.GroundObject == HeldObject ) return false;
		if ( _carryGrace > 0 ) return true;

		var error = body.PhysicsBody.MassCenter.Distance( _carryTarget );
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
		_carryTarget = target;
		MoveCarriedBody( body, target, eye.Rotation * _holdOffset );
	}

	void MoveCarriedBody( Rigidbody body, Vector3 target, Rotation orientation )
	{
		var velocity = CarryVelocity( body.Velocity, target - body.PhysicsBody.MassCenter, MaxCarrySpeed, MaxCarrySpeed * 2 );
		var contact = Scene.Trace.Body( body, body.PhysicsBody.Position + velocity * Time.Delta )
			.IgnoreGameObjectHierarchy( HeldObject ).WithoutTags( "trigger" ).Run();
		var other = contact.GameObject?.GetComponentInParent<Rigidbody>();
		bool obstructed = contact.Hit && (other is not { MotionEnabled: true } || other.Mass > body.Mass);
		if ( obstructed )
			velocity -= contact.Normal * (MathF.Min( velocity.Dot( contact.Normal ), 0 ) * (1 - contact.Fraction));
		body.Velocity = velocity;

		_carryTurnRate = _carryTurnRate.Approach( obstructed ? 0.1f : 1, 2 * Time.Delta );
		var rotation = Rotation.Difference( body.WorldRotation, orientation );
		var spinAxis = new Vector3( rotation.x, rotation.y, rotation.z ).Normal;
		var angle = rotation.Angle().DegreeToRadian() * (rotation.w < 0 ? -1 : 1);
		var spinLimit = MaxCarrySpin * _carryTurnRate * _carryTurnRate * _carryTurnRate;
		body.AngularVelocity = CarryVelocity( body.AngularVelocity, spinAxis * angle, spinLimit, MaxCarrySpin * 2 );
	}

	static Vector3 CarryVelocity( Vector3 velocity, Vector3 delta, float limit, float dampingLimit ) =>
		velocity - velocity.ClampLength( dampingLimit ) + (delta / Time.Delta).ClampLength( limit );

	void Release()
	{
		var held = HeldObject;
		HeldObject = null;
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
		var ready = !IsProxy && !HeldObject.IsValid() && IsHeld && FindBody( TraceLength, out _ ) is { Mass: <= MaxMass } body && !IsFlesh( body );
		_effects.Update( this, HeldObject, ready );
	}

	protected override void OnViewModelPlaced( ViewModel model ) => _effects?.Follow( this );

	public override void DrawCrosshair( Painter painter, Vector2 center ) => Crosshair.Draw( painter, center, CanPrimaryAttack(), 0 );

	protected override bool OnHolstering( BaseInventoryItem next ) => !HeldObject.IsValid() && base.OnHolstering( next );

	protected override void OnHolstered()
	{
		if ( Networking.IsHost ) Release();
		base.OnHolstered();
	}

	protected override void OnDisabled()
	{
		_effects?.Dispose();
		_effects = null;
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		if ( Networking.IsHost ) Release();
		base.OnDestroy();
	}
}

/// <summary>
/// The physcannon's code-driven sprites and beams, reconstructed from the SDK's StartEffects and
/// DoEffect states: a core glow at the first attachment, glows on every prong, endcaps and beams
/// from the prong tips while carrying. Like Source's sprite and beam entities they draw in the world
/// pass at the attachments' positions, so the viewmodel covers them. The world model only has "core".
/// </summary>
sealed class PhysCannonEffects : IDisposable
{
	// v_physcannon's prong attachments from the SDK's StartEffects; the world model only has "core".
	static readonly string[] Prongs = ["fork1b", "fork1m", "fork1t", "fork2b", "fork2m", "fork2t"];
	static readonly string[] Tips = ["fork1t", "fork2t"];
	const float BeamLength = 250;
	// StartEffects tints the prong glows kRenderTransAdd 255, 128, 0.
	static readonly Color GlowColor = new( 1, 0.5f, 0 );
	SkinnedModelRenderer _renderer;
	ViewModel _view;
	GameObject _root;
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

	public void Launch( bool fromHold )
	{
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
		if ( !_root.IsValid() ) Build( renderer, model as ViewModel, mount );
		var holding = held.IsValid();
		UpdateElements( weapon, holding, ready );
		if ( holding && _holdSound is null && Sounds.Sound( "Weapon_PhysCannon.HoldSound", mount ) is { } hold ) _holdSound = weapon.GameObject.PlaySound( hold );
		if ( !holding && _holdSound is not null )
		{
			_holdSound.Stop( 0.2f );
			_holdSound = null;
		}
		if ( holding ) MountedEffects.SetSprite( _core, 0.2f, 255 );
		else if ( ready ) MountedEffects.SetSprite( _core, 0.15f, 128 );
		else MountedEffects.SetSprite( _core, 0, 0 );
		// DoEffectLaunch: the blast grows to scale 2 while fading out over 0.1s.
		var blast = _launch / 0.1f;
		MountedEffects.SetSprite( _blast, blast < 1 ? float.Lerp( 0.1f, 2, blast ) : 0, 255 * (1 - blast) );
		foreach ( var glow in _glows )
		{
			if ( holding ) MountedEffects.SetSprite( glow, 0.5f, 64, GlowColor );
			else if ( ready ) MountedEffects.SetSprite( glow, 0.4f, 32, GlowColor );
			else MountedEffects.SetSprite( glow, Game.Random.Float( 0.3f, 0.35f ), Game.Random.Float( 16, 24 ), GlowColor );
		}
		foreach ( var endcap in _endcaps )
			MountedEffects.SetSprite( endcap, holding ? Game.Random.Float( 0.1f, 0.15f ) : 0, Game.Random.Float( 200, 255 ) );
		var end = holding ? held.GetBounds().Center : Vector3.Zero;
		foreach ( var (tip, beam) in _beams )
		{
			if ( !tip.IsValid() ) continue;
			var start = Place( tip );
			if ( blast < 1 ) MountedEffects.SetBeam( beam, start, start + weapon.AimRay.Forward * BeamLength, 6.4f, 255 );
			else MountedEffects.SetBeam( beam, start, end, holding ? Game.Random.Float( 1, 2 ) : 0, 128 );
		}
		Follow( weapon );
	}

	/// <summary>
	/// Moves the effects onto the attachments. The weapon also calls this once the viewmodel is placed for the frame,
	/// which happens after updates, so they don't trail the view.
	/// </summary>
	public void Follow( GravityWeapon weapon )
	{
		foreach ( var (sprite, anchor) in _anchors )
		{
			if ( !sprite.IsValid() || !anchor.IsValid() ) continue;
			if ( _view.IsValid() ) _view.PlaceBehind( sprite.GameObject, Place( anchor ) );
			else sprite.WorldPosition = Place( anchor );
		}
		foreach ( var (tip, beam) in _beams )
		{
			if ( !tip.IsValid() || !beam.IsValid() ) continue;
			beam.VectorPoints[0] = Place( tip );
			if ( _launch < 0.1f ) beam.VectorPoints[1] = beam.VectorPoints[0] + weapon.AimRay.Forward * BeamLength;
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
			if ( _view.IsValid() )
			{
				_view.OnClawsChanged();
				if ( Sounds.WeaponSound( weapon, open ? "special2" : "melee_hit" ) is { } sound )
					_renderer.GameObject.PlaySound( sound );
			}
			current = Element();
		}
		_view?.SetPoseParameter( "active", current );
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

	void Build( SkinnedModelRenderer renderer, ViewModel view, string mount )
	{
		_view = view;
		var viewModel = view.IsValid();
		_root = new GameObject( true, "Physcannon effects" );
		_root.NetworkMode = NetworkMode.Never;
		var coreName = renderer.Model.Attachments.All.FirstOrDefault()?.Name;
		var core = coreName is null ? renderer.GameObject : renderer.GetAttachmentObject( coreName ) ?? renderer.GameObject;
		_core = AddSprite( core, "sprites/orangecore1", mount );
		_blast = AddSprite( core, "sprites/orangecore2", mount );
		// sprites/glow04_noz names the glow04 texture.
		foreach ( var prong in Prongs.Select( renderer.GetAttachmentObject ).Where( x => x.IsValid() ) )
			if ( AddSprite( prong, "sprites/glow04", mount ) is { } glow ) _glows.Add( glow );
		var tips = viewModel ? Tips.Select( renderer.GetAttachmentObject ).Where( x => x.IsValid() ).ToArray() : [core];
		foreach ( var tip in tips )
		{
			if ( viewModel && AddSprite( tip, "sprites/orangeflare1", mount ) is { } endcap ) _endcaps.Add( endcap );
			if ( MountedEffects.AddBeam( _root, "sprites/orangelight1", mount ) is { } beam ) _beams.Add( (tip, beam) );
		}
	}

	public void Dispose()
	{
		_holdSound?.Stop( 0.2f );
		_holdSound = null;
		_root?.Destroy();
		_root = null;
		_view = null;
		_glows.Clear();
		_endcaps.Clear();
		_beams.Clear();
		_anchors.Clear();
	}
}
