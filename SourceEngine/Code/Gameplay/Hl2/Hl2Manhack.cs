using System;

namespace Source1;

/// <summary>
/// npc_manhack's blades: one sk_manhack_melee_dmg slice per touch, at most every 0.3 s, a 200 unit/s bounce away from
/// whatever it slices, and a jittery approach. Its sparks: ManhackSparks and NPC_Manhack.Grind where the blades
/// meet a wall or something that does not bleed, g_pEffects->Sparks when clubbed and on death, and a smoke trail
/// once down to half health.
/// </summary>
[Title( "HL2 Manhack" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Manhack : Component, ISourceNpcPart
{
	/// <summary>How far from its centre it cuts, set by eye: the nose of the model, so a hit lands as it touches the body.</summary>
	const float SliceReach = 7;

	/// <summary>How far ahead the blades grind on a wall.</summary>
	const float BladeReach = 22;

	/// <summary>How fast a hit knocks it back. The flying layer brakes that over about 70 units, then it comes in again.</summary>
	const float BounceSpeed = 240;
	const float SliceInterval = 0.3f;
	const float GrindInterval = 0.2f;

	/// <summary>Approximation: Source's flight noise comes from its physics controller; this wanders the goal instead.</summary>
	const float Wander = 40;

	/// <summary>The wander while it goes for a target, small enough that it still flies into it.</summary>
	const float AttackWander = 10;

	/// <summary>Hack: nothing spaces flyers, so manhacks steer their goals apart when they come this close to each other.</summary>
	const float Spacing = 40;

	Vector3 _wander;
	Vector3 _wanderGoal;
	Vector3 _apart;
	bool _attacking;
	Vector3 _heading;
	Vector3 _lastPosition;
	GameObject _smoke;
	TimeSince _lastWander;
	TimeSince _lastSlice = 10;
	TimeSince _lastGrind = 10;
	SourceCreatureNpc _npc;

	SourceCreatureNpc Npc => _npc.IsValid() ? _npc : _npc = GetComponent<SourceCreatureNpc>();
	string Mount => SourceMount.Of( GameObject );

	/// <summary>Where to steer this frame, added to the creature's flight goal.</summary>
	public Vector3 Offset => _wander + _apart;

	/// <summary>How close to its flight goal it stops while it goes for a target: it flies in until the blades touch.</summary>
	public float AttackStopDistance => 4;

	Sandbox.Npcs.Layers.FlyingLayer Flying => Npc?.Movement as Sandbox.Npcs.Layers.FlyingLayer;

	protected override void OnStart() => _lastPosition = WorldPosition;

	protected override void OnUpdate()
	{
		if ( Npc.IsValid() && Npc.Renderer.IsValid() )
		{
			// CNPC_Manhack::SpinBlades shows the blur once its flying engine is up to speed.
			Npc.Renderer.SetBodyGroup( "Blade", 0 );
			Npc.Renderer.SetBodyGroup( "BladeBlur", Npc.Health > 0 && !Npc.PhysicsCarrier.IsValid() ? 1 : 0 );
		}

		// CNPC_Manhack::OnTakeDamage_Alive's smoke trail below half health.
		if ( !_smoke.IsValid() && Npc is { Health: > 0 } npc && npc.Health <= npc.MaxHealth * 0.5f )
			_smoke = Hl2RocketTrail.Create( GameObject, Vector3.Zero, Mount );

		if ( IsProxy )
			return;

		Bump();

		// A new spot to drift toward now and then, eased so the flight goal never jumps.
		if ( _lastWander > 0.5f )
		{
			_lastWander = 0;
			_wanderGoal = Vector3.Random * (_attacking ? AttackWander : Wander);
		}
		_wander = _wander.LerpTo( _wanderGoal, Time.Delta * 4 );

		var apart = Vector3.Zero;
		foreach ( var other in Scene.GetAllComponents<Hl2Manhack>() )
		{
			if ( other == this ) continue;
			var from = WorldPosition - other.WorldPosition;
			var distance = from.Length;
			if ( distance < Spacing && distance > 0.01f )
				apart += from.Normal * (Spacing - distance);
		}
		_apart = _apart.LerpTo( apart, Time.Delta * 6 );
	}

	/// <summary>Bounce: a knock the flying layer flies off, instead of moving the manhack ourselves.</summary>
	void Bounce( Vector3 direction )
	{
		if ( Flying is { } flying )
			flying.Velocity = direction * BounceSpeed;
	}

	void Bump()
	{
		var moved = WorldPosition - _lastPosition;
		_lastPosition = WorldPosition;
		if ( moved.Length > 0.01f )
			_heading = moved.Normal;
		if ( _heading.IsNearlyZero() || Npc is not { Health: > 0 } )
			return;

		// CNPC_Manhack::Bump: something solid the blades reach, whose plane is within 0.25 of the blade plane.
		var center = GameObject.GetBounds().Center;
		var tr = Scene.Trace.Ray( center, center + _heading * BladeReach ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "player", "npc", "ragdoll" ).Run();
		if ( !tr.Hit || MathF.Abs( WorldRotation.Up.Dot( tr.Normal ) ) >= 0.25f )
			return;

		var direction = SparkDirection( tr.Normal );
		Bounce( tr.Normal );
		_heading = tr.Normal;
		if ( _lastGrind < GrindInterval )
			return;

		_lastGrind = 0;
		Grind( tr.EndPosition, direction );
	}

	/// <summary>ManhackSparks' direction: halfway between the surface normal and the flight direction.</summary>
	Vector3 SparkDirection( Vector3 normal )
	{
		var direction = normal + _heading;
		return direction.IsNearlyZero() ? normal : direction.Normal;
	}

	/// <summary>Host: slice the target while touching it, then bounce off.</summary>
	public void Contact( GameObject target )
	{
		_attacking = target.IsValid();
		if ( !target.IsValid() || !Npc.IsValid() )
			return;

		var center = WorldPosition;
		if ( !SourceNpcs.Touches( GameObject, target, center, SliceReach, out var closest ) )
			return;

		var away = (center - target.GetBounds().Center).WithZ( 0 ).Normal;
		Bounce( away );
		if ( _lastSlice < SliceInterval )
			return;

		// Each hit ends the victim's schedule, so per-frame damage would pin it in place.
		_lastSlice = 0;
		Npc.Damage( target, center, closest, "melee" );

		// Source sparks on anything that does not bleed; here, a hit surface that is not flesh.
		var tr = Scene.Trace.Ray( center, closest + (closest - center).Normal * 8 ).IgnoreGameObjectHierarchy( GameObject ).Run();
		var bleeds = !tr.Hit || tr.GameObject?.Root != target.Root || (tr.Surface?.ResourceName ?? "").Contains( "flesh" );
		if ( bleeds )
			Slice( center );
		else
			Grind( tr.EndPosition, SparkDirection( tr.Normal ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Slice( Vector3 position )
	{
		if ( SourceSounds.Sound( "NPC_Manhack.Slice", Mount ) is { } sound )
			Sound.Play( sound, position );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Grind( Vector3 position, Vector3 direction )
	{
		if ( SourceSounds.Sound( "NPC_Manhack.Grind", Mount ) is { } sound )
			Sound.Play( sound, position );
		SourceWeaponEffects.SparkFan( position, direction, Mount );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Sparks( Vector3 position, int magnitude, Vector3 direction ) => SourceWeaponEffects.ElectricSpark( position, magnitude, direction, Mount );

	void ISourceNpcPart.OnNpcHurt( Component.IDamageable.DamageEvent damage )
	{
		// CNPC_Manhack::TraceAttack: clubbed, it sparks where it was hit.
		var info = damage.DamageInfo;
		var clubbed = info.Tags.Has( "melee" ) || SourceWeaponSetup.IsMelee( info.Weapon?.GetComponent<BaseCombatWeapon>() );
		if ( clubbed )
			Sparks( info.Position, 1, (info.Origin - info.Position).Normal );
	}

	void ISourceNpcPart.OnNpcDeath( Component.IDamageable.DeathEvent death )
	{
		for ( var i = 0; i < 3; i++ )
			Sparks( WorldPosition + new Vector3( Game.Random.Float( -12, 12 ), Game.Random.Float( -12, 12 ), Game.Random.Float( -12, 12 ) ), 2, Vector3.Zero );
	}
}

/// <summary>
/// npc_cscanner's photo: the eye flash (sprites/blueflare1 at scale 1.4, kRenderGlow) and its TakePhoto sound. A glow sprite draws over
/// the model and keeps the screen size it has at 200 units. BlindFlashTarget whites out the photographed player's screen.
/// </summary>
[Title( "HL2 Scanner Photo" )]
[Category( "Half-Life 2" )]
public sealed class Hl2ScannerPhoto : Component
{
	/// <summary>SCANNER_FLASH_MAX_VALUE, cut to 0.9 under HDR.</summary>
	const float BlindAlpha = 240 / 255f * 0.9f;

	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void Flash( GameObject target )
	{
		var mount = SourceMount.Of( GameObject );
		if ( SourceSounds.Sound( "NPC_CScanner.TakePhoto", mount ) is { } sound )
			GameObject.PlaySound( sound );

		Blind( target );

		var renderer = GetComponent<Sandbox.Npcs.BaseNpc>()?.Renderer;
		var light = renderer?.GetAttachmentObject( "light" ) ?? GameObject;
		if ( Application.IsDedicatedServer || !light.IsValid() )
			return;

		var go = new GameObject( light, true, "Scanner flash" );
		go.NetworkMode = NetworkMode.Never;
		var distance = 200f;
		if ( Scene.Camera is { } camera )
		{
			// Brought out of the shell toward the eye, as the glow draws without depth.
			var toCamera = camera.WorldPosition - light.WorldPosition;
			distance = MathF.Max( toCamera.Length, 1 );
			go.WorldPosition = light.WorldPosition + toCamera.Normal * MathF.Min( 32, distance * 0.5f );
		}

		// A 64 px sprite at scale 1.4, given as a radius.
		go.AddComponent<SourceSpriteBurst>().Add( "sprites/blueflare1", mount, 0, 64 * 1.4f * 0.5f * distance / 200, Color.White, 0.3f );
	}

	/// <summary>
	/// BlindFlashTarget, for the local player when photographed: looking within 60 degrees of the scanner with nothing opaque
	/// between, a white UTIL_ScreenFade as strong as the look is square, held half a second and fading over three.
	/// </summary>
	void Blind( GameObject target )
	{
		var player = target.IsValid() ? target.Root.GetComponentInChildren<PlayerController>() : null;
		if ( !player.IsValid() || player.IsProxy || Scene.Camera is not { } camera )
			return;

		var eye = player.EyeTransform;
		var dot = (WorldPosition - eye.Position).Normal.Dot( eye.Rotation.Forward );
		if ( dot <= 0.5f )
			return;

		if ( Scene.Trace.Ray( WorldPosition, eye.Position ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "player", "npc", "trigger" ).Run().Hit )
			return;

		SourceScreenFade.Show( camera, Color.White.WithAlpha( BlindAlpha * dot ), 0.5f, 3 );
	}
}
