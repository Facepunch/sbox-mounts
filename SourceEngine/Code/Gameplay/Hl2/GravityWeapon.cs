using System;

namespace Source1.Hl2;

/// <summary>
/// weapon_physcannon: punts with attack1, pulls and carries with attack2. Values follow the SDK
/// (physcannon_tracelength 250, punt impulse per mass, 250 kg carry limit); the body is held with
/// <see cref="PhysicsHold"/> on the host, which simulates it. An NPC it carries is told through
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

	const float TraceLength = 250;
	const float PullLength = TraceLength * 4;
	const float PullForce = 4000;
	const float MaxMass = 250;
	const float MinLaunchSpeed = 700;
	const float MaxLaunchSpeed = 1500;
	[Sync( SyncFlags.FromHost )] public GameObject HeldObject { get; private set; }
	[Sync] public bool Pulling { get; set; }
	readonly PhysicsHold _hold = new();
	Rotation _holdOffset;
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

	Rigidbody FindBody( float range, out Vector3 hitPosition, bool punt = false )
	{
		var ray = Scene.Trace.Ray( AimRay, range ).IgnoreGameObjectHierarchy( GameObject.Root ).WithoutTags( "trigger", "player", "projectile" );
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

	static bool CanMove( Rigidbody body ) => body.IsValid() && body.MotionEnabled && !body.IsProxy && body.PhysicsBody.IsValid();

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
			var spin = new Vector3( Game.Random.Float( -600, 600 ), Game.Random.Float( -600, 600 ), Game.Random.Float( -600, 600 ) );
			held.AngularVelocity = held.WorldRotation * spin * (MathF.PI / 180);
			PuntEffects( PuntResult.LaunchHeld );
			return;
		}
		var body = FindBody( TraceLength, out var hit, punt: true );
		if ( !CanMove( body ) )
		{
			PuntEffects( PuntResult.Miss );
			return;
		}
		// PuntVPhysics: a 15000 impulse at the centre plus 600 per kg (up to 250 kg) at the hit point, flattened up off the floor.
		if ( forward.z < 0 ) forward = forward.WithZ( forward.z * -0.65f );
		body.ApplyImpulse( forward * 15000 );
		body.ApplyImpulseAt( hit, forward * (MathF.Min( body.Mass, MaxMass ) * 600) );
		PuntEffects( PuntResult.Hit );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Drop()
	{
		if ( !HeldObject.IsValid() ) return;
		Release();
		CarryEffects( pickup: false );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void CarryEffects( bool pickup )
	{
		if ( pickup ) SetNextSecondaryFire( 0.5f );
		else SetNextFire( 0.5f );
		(WeaponModel as ViewModel)?.PlayGravityGunAttack();
		if ( Sounds.WeaponSound( this, pickup ? "special1" : "melee_miss" ) is { } sound ) GameObject.PlaySound( sound );
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
		var body = FindBody( PullLength, out var hit );
		if ( !CanMove( body ) || body.Mass > MaxMass )
		{
			if ( _playedDenySound ) return;
			_playedDenySound = true;
			DenyPickup();
			return;
		}
		var eye = AimRay.Position;
		if ( eye.Distance( hit ) > TraceLength )
		{
			var direction = (eye - body.GameObject.GetBounds().Center).Normal;
			var scale = body.Mass < 50 ? (body.Mass + 0.5f) / 50 : 1;
			body.ApplyImpulse( direction * (PullForce * scale) );
			return;
		}
		HeldObject = body.GameObject;
		_holdOffset = PhysicsHold.Stabilize( Rotation.LookAt( AimRay.Forward ) ).Inverse * body.WorldRotation;
		_hold.Hold( body, body.PhysicsBody.MassCenter );
		foreach ( var npc in HeldObject.Root.GetComponentsInChildren<Sandbox.Npcs.BaseNpc>() )
			npc.StartCarry( GameObject );
		CarryEffects( pickup: true );
	}

	void Hold()
	{
		var body = HeldObject.GetComponent<Rigidbody>();
		if ( !Active || !CanMove( body ) )
		{
			Release();
			return;
		}
		var aim = AimRay;
		// UpdateObject: 24 + 2 * (player radius 16 + the object's extent along the aim).
		var extents = HeldObject.GetBounds().Size * 0.5f;
		var forward = aim.Forward;
		var radial = MathF.Abs( forward.x ) * extents.x + MathF.Abs( forward.y ) * extents.y + MathF.Abs( forward.z ) * extents.z;
		var distance = 24 + 2 * (16 + radial);
		var target = aim.Position + forward * distance;
		if ( body.PhysicsBody.MassCenter.Distance( target ) > PullLength )
		{
			Release();
			return;
		}
		_hold.Move( new Transform( target, PhysicsHold.Stabilize( Rotation.LookAt( forward ) ) * _holdOffset ) );
	}

	void Release()
	{
		_hold.Release();
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
		var ready = !IsProxy && !HeldObject.IsValid() && IsHeld && FindBody( TraceLength, out _ ) is { Mass: <= MaxMass };
		_effects.Update( this, HeldObject, ready );
	}

	protected override void OnViewModelPlaced( ViewModel model ) => _effects?.Follow( this );

	public override void DrawCrosshair( Painter painter, Vector2 center ) => Crosshair.Draw( painter, center, CanPrimaryAttack(), 0 );

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
