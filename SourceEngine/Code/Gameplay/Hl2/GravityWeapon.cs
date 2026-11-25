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

	const float TraceLength = 250;
	const float PullLength = TraceLength * 4;
	const float PullForce = 4000;
	const float MaxMass = 250;
	const float MinLaunchSpeed = 700;
	const float MaxLaunchSpeed = 1500;
	const float SourceGravity = 600;
	[Sync( SyncFlags.FromHost )] public GameObject HeldObject { get; private set; }
	[Sync] public bool Pulling { get; set; }
	readonly PhysicsHold _hold = new();
	Rotation _holdOffset;
	// m_nAttack2Debounce: after a drop or launch, attack2 must be released before pulling again.
	bool _attack2Debounce;
	PhysCannonEffects _effects;
	TimeUntil _tooHeavyCooldown;

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
		SetNextFire( 0.5f );
		if ( HeldObject.IsValid() ) _attack2Debounce = true;
		Punt();
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( 0.3f );
		if ( !HeldObject.IsValid() ) return;
		_attack2Debounce = true;
		Drop();
	}

	Rigidbody FindBody( float range, out Vector3 hitPosition )
	{
		// FindObjectTrace: a line first, then a small hull for thin or near-miss aims.
		var aim = AimRay;
		var tr = Scene.Trace.Ray( aim, range ).IgnoreGameObjectHierarchy( GameObject.Root ).WithoutTags( "trigger", "player", "projectile" ).Run();
		if ( tr.GameObject?.GetComponentInParent<Rigidbody>() is null )
			tr = Scene.Trace.Ray( aim, range ).Size( 8 ).IgnoreGameObjectHierarchy( GameObject.Root ).WithoutTags( "trigger", "player", "projectile" ).Run();
		hitPosition = tr.HitPosition;
		var body = tr.GameObject?.GetComponentInParent<Rigidbody>();
		return body.IsValid() && body.MotionEnabled ? body : null;
	}

	static bool CanMove( Rigidbody body ) => body.IsValid() && body.MotionEnabled && !body.IsProxy && body.PhysicsBody.IsValid();

	// Approximation: sandbox gravity is stronger than HL2's sv_gravity, so launches are scaled to carry as far.
	float LaunchScale => MathF.Sqrt( Scene.PhysicsWorld.Gravity.Length / SourceGravity );

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Punt()
	{
		var forward = AimRay.Forward;
		if ( HeldObject.IsValid() )
		{
			var held = HeldObject.GetComponent<Rigidbody>();
			Release();
			if ( !held.IsValid() ) return;
			// Pickup_DefaultPhysGunLaunchVelocity: physcannon_maxforce up to 100 kg, splined down to physcannon_minforce at 600.
			var t = Math.Clamp( (held.Mass - 100) / 500, 0, 1 );
			held.Velocity += forward * float.Lerp( MaxLaunchSpeed, MinLaunchSpeed, t * t * (3 - 2 * t) ) * LaunchScale;
			PlayEffect( "single_shot", true );
			return;
		}
		var body = FindBody( TraceLength, out _ );
		if ( !CanMove( body ) )
		{
			PlayEffect( "empty", false );
			return;
		}
		// PuntVPhysics: a 15000 impulse at the centre plus 600 per kg (up to 250 kg) at the hit point, flattened up off the floor.
		if ( forward.z < 0 ) forward = forward.WithZ( forward.z * -0.65f );
		var mass = MathF.Max( body.Mass, 1 );
		body.Velocity += forward * ((15000 + MathF.Min( mass, MaxMass ) * 600) / mass * LaunchScale);
		PlayEffect( "single_shot", true );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Drop()
	{
		if ( !HeldObject.IsValid() ) return;
		Release();
		PlayEffect( "melee_miss", false );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayEffect( string role, bool launch )
	{
		if ( Sounds.WeaponSound( this, role ) is { } sound ) GameObject.PlaySound( sound );
		if ( launch ) _effects?.Launch();
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !Networking.IsHost ) return;
		if ( HeldObject.IsValid() )
		{
			Hold();
			return;
		}
		if ( Pulling && Active ) Pull();
	}

	void Pull()
	{
		var body = FindBody( PullLength, out var hit );
		if ( !CanMove( body ) ) return;
		var eye = AimRay.Position;
		if ( eye.Distance( hit ) > TraceLength )
		{
			// Approximation: the SDK pulls with physcannon_pullforce until the object is in reach.
			var direction = (eye - body.WorldPosition).Normal;
			body.Velocity += direction * (PullForce / MathF.Max( body.Mass, 1 )) * Time.Delta * 6;
			return;
		}
		if ( body.Mass > MaxMass )
		{
			if ( _tooHeavyCooldown > 0 ) return;
			_tooHeavyCooldown = 1;
			PlayEffect( "special3", false );
			return;
		}
		HeldObject = body.GameObject;
		_holdOffset = PhysicsHold.Stabilize( Rotation.LookAt( AimRay.Forward ) ).Inverse * body.WorldRotation;
		_hold.Hold( body, body.PhysicsBody.MassCenter );
		foreach ( var npc in HeldObject.Root.GetComponentsInChildren<Sandbox.Npcs.BaseNpc>() )
			npc.StartCarry( GameObject );
		PlayEffect( "special1", false );
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
		var ready = !IsProxy && !HeldObject.IsValid() && IsHeld && FindBody( TraceLength, out _ ) is not null;
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
	bool _ready;
	bool _open;
	float _elementFrom;
	TimeSince _elementMoved = 10;
	SoundHandle _holdSound;

	public void Launch() => _launch = 0;

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
		if ( ready != _ready && _view.IsValid() && held is null )
		{
			// OpenElements and CloseElements play the claw sounds as the prongs react.
			if ( Sounds.WeaponSound( weapon, ready ? "special2" : "melee_hit" ) is { } sound ) renderer.GameObject.PlaySound( sound );
		}
		_ready = ready;
		var holding = held.IsValid();
		UpdateElements( _view, holding || ready );
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
	void UpdateElements( ViewModel view, bool open )
	{
		var current = Element();
		if ( open != _open )
		{
			_elementFrom = current;
			_elementMoved = 0;
			_open = open;
			current = Element();
		}
		view?.SetPoseParameter( "active", current );
	}

	float Element()
	{
		var t = Math.Clamp( _elementMoved / 0.2f, 0, 1 );
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
