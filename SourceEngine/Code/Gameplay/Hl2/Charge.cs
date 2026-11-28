using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace Source1.Hl2;

/// <summary>
/// The antlion guard's and the hunter's charge (defaults are the guard's): it faces an enemy 256 units to <see cref="MaxRange"/> away,
/// then runs at it on the model's *_CHARGE_START and *_CHARGE_RUN, carried by their ground motion, turning too slowly up close to
/// follow a late sidestep. Enemies it reaches are hit, antlions trampled, light props ploughed through, walls and heavy props crashed into.
/// </summary>
[Title( "HL2 Charge" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2Charge", "Hl2Charge" )]
public sealed class Charge : CreatureBehaviour, Component.IDamageable.IEvents
{
	const float ChargeMin = 256;
	const float NonPlayerDamage = 250;
	const float StepHeight = 18;
	const float ProbeLift = StepHeight * 2;
	const float CloseRange = 512;
	const float PassedDot = 0.25f;
	const float CarryOnChance = 0.3f;
	const float HitCooldown = 2;
	const float HitForce = 75 * 700;
	/// <summary>Push impulse per unit of strength: a 15 kg prop beside a crash leaves at about 500 units a second, 100 kg barely moves.</summary>
	const float PushScale = 16;
	/// <summary>The most speed a push adds to a prop, units a second.</summary>
	const float MaxPushSpeed = 600;
	/// <summary>A skid slower than this, units a second, is spent: it just stops against what it meets.</summary>
	const float MinSkidSpeed = 100;
	/// <summary>A drop or rise past this from where a step meant to go is a ledge: it stops there rather than crashing.</summary>
	const float LedgeHeight = 50;

	[Property] public float MaxRange { get; set; } = 2048;
	/// <summary>Seconds after a charge before the next, plus up to <see cref="RandomDelay"/> more.</summary>
	[Property] public float Delay { get; set; } = 4;
	[Property] public float RandomDelay { get; set; }
	/// <summary>The odds of charging when it could, rolled once a second.</summary>
	[Property] public float Chance { get; set; } = 1;
	/// <summary>The skill setting for what a hit does to a player; anything else takes 250.</summary>
	[Property] public string DamageSetting { get; set; } = "sk_antlionguard_dmg_charge";
	[Property] public string HitSound { get; set; } = "NPC_AntlionGuard.Shove";
	[Property] public string CrashSound { get; set; } = "NPC_AntlionGuard.HitHard";

	/// <summary>Degrees a second turning in place, with a turn animation and without; then winding up, and running at a player beyond 512 units and within.</summary>
	[Property, Group( "Turning" )] public float FaceTurnRate { get; set; } = 400;
	[Property, Group( "Turning" )] public float FaceTurnRateStill { get; set; } = 200;
	[Property, Group( "Turning" )] public float WindUpTurnRate { get; set; } = 40;
	[Property, Group( "Turning" )] public float RunTurnRate { get; set; } = 160;
	[Property, Group( "Turning" )] public float CloseTurnRate { get; set; } = 25;
	/// <summary>Degrees a second throughout when the enemy is not a player; 0 turns as at a player.</summary>
	[Property, Group( "Turning" )] public float NpcTurnRate { get; set; } = 160;

	/// <summary>Moveable props up to this share of its own mass don't stop it.</summary>
	[Property] public float LightPropRatio { get; set; } = 0.5f;

	/// <summary>
	/// The guard's way: it braces, pushes props near a blocked step aside, smashes what is ahead when it crashes, throws NPCs it hits,
	/// steers round the world only and may run on past the enemy. Otherwise it steers round all but light props and breaks weak ones.
	/// </summary>
	[Property] public bool Smashes { get; set; } = true;
	/// <summary>Width of the body the charge sweeps; 0 is the collider's.</summary>
	[Property] public float HullWidth { get; set; }
	/// <summary>Bullet and melee damage taken during the run is multiplied by this.</summary>
	[Property] public float RunDamageScale { get; set; } = 1;
	[Property, Group( "Crash" )] public float CrashShakeAmplitude { get; set; } = 32;
	[Property, Group( "Crash" )] public float CrashShakeRadius { get; set; } = 512;

	public enum Phase { None, Face, Start, Run, Stop, Crash }
	[Sync( SyncFlags.FromHost )] Phase State { get; set; }
	[Sync( SyncFlags.FromHost )] string Clip { get; set; }
	public bool Charging => State != Phase.None;
	internal TimeSince SinceHit => _sinceHit;

	ModelSequences _sequences;
	string _start, _run, _stop, _turnLeft, _turnRight, _anticipation, _hitGesture;
	SourceModelSequenceData[] _crashes = [];
	float _chargeSpeed, _mass, _radius, _height, _time, _lastTime;
	TimeUntil _nextCharge;
	TimeSince _sinceHit = 10, _sinceAnticipation = 10, _sinceHitGesture = 10, _sincePush = 10;
	GameObject _target;
	bool _passed;
	readonly Dictionary<GameObject, TimeUntil> _passing = [];
	MovementLayer _walker;
	ChargeMover _mover;
	IReadOnlyDictionary<string, float> _skill = new Dictionary<string, float>();

	public override IEnumerable<(string Name, bool Loop)> AnimationChoices =>
		[(_turnLeft, true), (_turnRight, true), (_start, false), (_run, true), (_stop, false), .. _crashes.Select( x => (x.Name, false) )];

	public override IEnumerable<string> GestureChoices => [_anticipation, _hitGesture];

	public override string Animation => State == Phase.None ? null : Clip;

	protected override async void OnStart() => _skill = await SkillData.Read( MountContent.Of( GameObject ), "skill_episodic.cfg" );

	protected override void OnConfigure( Model model, ModelSequences sequences )
	{
		_sequences = sequences;
		var start = sequences.All.FirstOrDefault( s => s.Activity?.EndsWith( "_CHARGE_START", StringComparison.Ordinal ) == true )?.Activity;
		var prefix = start?[..^"START".Length];
		_start = sequences.FindActivity( start );
		_run = sequences.FindActivity( prefix + "RUN" );
		_stop = sequences.FindActivity( prefix + "STOP" );
		_crashes = sequences.All.Where( s => !s.Delta && s.Activity == prefix + "CRASH" && model.AnimationClips().ContainsKey( s.Name ) ).DistinctBy( s => s.Name ).ToArray();
		_turnLeft = sequences.FindActivity( "ACT_TURN_LEFT" );
		_turnRight = sequences.FindActivity( "ACT_TURN_RIGHT" );
		// Only the guard braces and flinches on a hit; the hunter's model has a hit gesture it never plays
		_anticipation = Smashes && prefix is not null ? FindGesture( model, sequences, prefix + "ANTICIPATION" ) : null;
		_hitGesture = Smashes && prefix is not null ? FindGesture( model, sequences, prefix + "HIT" ) : null;
		_chargeSpeed = sequences.Get( _run )?.GroundSpeed ?? 0;
		var mass = model.Physics?.Parts.Sum( x => x.Mass ) ?? 0;
		_mass = mass > 0 ? mass : 1000;
	}

	Vector3 Centre => WorldPosition + Vector3.Up * (_height * 0.5f);

	float GroundSpeed => _sequences.Get( Clip ) is { GroundSpeed: > 0 } sequence ? sequence.GroundSpeed : _chargeSpeed;

	/// <summary>Host think; true from facing the enemy until the stop or crash is over.</summary>
	public override bool Think( GameObject target )
	{
		if ( _start is null || _run is null )
			return false;

		if ( State == Phase.None )
			return TryStart( target );

		_lastTime = _time;
		_time += Time.Delta;
		foreach ( var prop in _passing.Where( x => x.Value <= 0 ).Select( x => x.Key ).ToArray() )
			_passing.Remove( prop );

		// It ends for a new enemy or a dead one, not one out of sight; an enemy lost before the run leaves nothing to stop from.
		// Thrown or carried, physics has the body.
		var lost = Npcs.IsDead( _target ) || target.IsValid() && target != _target;
		if ( GetComponent<Rigidbody>() is { MotionEnabled: true } || lost && State == Phase.Face )
			Finish();
		else if ( State is Phase.Stop or Phase.Crash )
			Recover();
		else if ( lost )
			Enter( Phase.Stop, _stop );
		else if ( State == Phase.Face )
			Face();
		else
			Run();

		return State != Phase.None;
	}

	bool TryStart( GameObject target )
	{
		var npc = Npc;
		if ( !target.IsValid() || !_nextCharge || !npc.IsOnGround || _chargeSpeed < 1 )
			return false;
		var distance = WorldPosition.WithZ( 0 ).Distance( target.WorldPosition.WithZ( 0 ) );
		if ( distance < ChargeMin || distance > MaxRange )
			return false;
		var eye = WorldPosition + Vector3.Up * 60;
		if ( Scene.Trace.Ray( eye, Npcs.CapturePoint( target ) ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run() is { Hit: true } tr && tr.GameObject?.Root != target.Root )
			return false;
		if ( Game.Random.Float() >= Chance )
		{
			_nextCharge = 1;
			return false;
		}

		// The hull is the collider (else the model's bounds), a unit narrower so a body resting against a wall can still sweep along it
		var box = GetComponentInChildren<BoxCollider>();
		var hull = box.IsValid() ? BBox.FromPositionAndSize( box.Center, box.Scale ) : GameObject.GetLocalBounds();
		_radius = (HullWidth > 0 ? HullWidth : MathF.Max( hull.Size.x, hull.Size.y )) * 0.5f - 1;
		_height = hull.Maxs.z;

		_target = target;
		_passed = false;
		npc.Movement?.Stop();
		// The charge takes the body from navigation, and from the ground check that would hand it to physics over a prop
		_walker = npc.Movement;
		_mover ??= AddComponent<ChargeMover>( false );
		_mover.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		npc.SetMovement( _mover );
		Enter( Phase.Face, null );
		return true;
	}

	void Enter( Phase phase, string clip )
	{
		State = phase;
		Clip = clip;
		_time = 0;
		_lastTime = 0;
	}

	/// <summary>Host: cuts a charge short where it stands, as heavy damage does.</summary>
	internal void Interrupt()
	{
		if ( State != Phase.None )
			Finish();
	}

	/// <summary>Hands the body back to navigation and waits for the next charge.</summary>
	void Finish()
	{
		State = Phase.None;
		Clip = null;
		_passing.Clear();
		_nextCharge = Delay + Game.Random.Float( 0, RandomDelay );
		Npc.SetMovement( _walker );
	}

	/// <summary>Turns in place until it faces the enemy, on the turn animations where the model has them.</summary>
	void Face()
	{
		var yaw = YawTo( _target.WorldPosition );
		var offset = Angles.NormalizeAngle( yaw - WorldRotation.Yaw() );
		if ( MathF.Abs( offset ) <= 2 || _time > 3 )
		{
			Enter( Phase.Start, _start );
			return;
		}

		Clip = offset > 0 ? _turnLeft : _turnRight;
		Npcs.TurnToward( GameObject, yaw, NpcRate ?? (Clip is null ? FaceTurnRateStill : FaceTurnRate) );
	}

	/// <summary>The wind-up and the run: give up once past the enemy, steer, brace, and step along the animation.</summary>
	void Run()
	{
		// The enemy dropped behind its shoulder: it decides once per pass whether to swing round for another
		if ( WorldRotation.Forward.Dot( (_target.WorldPosition - WorldPosition).Normal ) >= PassedDot )
			_passed = false;
		else if ( !_passed )
		{
			_passed = true;
			if ( !Smashes || Game.Random.Float() >= CarryOnChance )
			{
				Enter( Phase.Stop, _stop );
				return;
			}
		}

		var close = WorldPosition.WithZ( 0 ).Distance( _target.WorldPosition.WithZ( 0 ) ) <= CloseRange;
		Npcs.TurnToward( GameObject, YawTo( _target.WorldPosition ) + Avoidance(), NpcRate ?? (State == Phase.Start ? WindUpTurnRate : close ? CloseTurnRate : RunTurnRate) );
		if ( Smashes )
			Anticipate();

		var phase = State;
		if ( Advance() is { } blocked )
			Blocked( blocked );
		if ( phase == Phase.Start && State == Phase.Start && _time >= Duration( _start, 0.5f ) )
			Enter( Phase.Run, _run );
	}

	/// <summary>
	/// The stop or the crash: the animation carries the body and nothing steers. A skid still at speed meets what is in its way
	/// as the run does, so a late sidestep in front of a wall ends in the crash.
	/// </summary>
	void Recover()
	{
		var speed = (Travelled( _time ).Position - Travelled( _lastTime ).Position).WithZ( 0 ).Length / MathF.Max( Time.Delta, 0.001f );
		if ( Advance() is { } blocked && State == Phase.Stop && speed > MinSkidSpeed )
			Blocked( blocked );
		if ( _time >= Duration( Clip, 0.5f ) )
			Finish();
	}

	float? NpcRate => !Npcs.IsPlayer( _target ) && NpcTurnRate > 0 ? NpcTurnRate : null;

	/// <summary>Degrees to steer off the line to the enemy, away from whichever side a probe 45 degrees out finds blocked.</summary>
	float Avoidance()
	{
		var length = 0.15f * GroundSpeed;
		var facing = WorldRotation.Yaw();
		var side = WorldRotation.Right * 0.5f;
		var steer = WorldRotation.Forward + side * (1 - Probe( facing + 45, length )) - side * (1 - Probe( facing - 45, length ));
		return Angles.NormalizeAngle( Rotation.LookAt( steer ).Yaw() - facing );
	}

	/// <summary>How far a steering probe gets, 0 to 1: the guard's sees only the world, the hunter's all but light props.</summary>
	float Probe( float yaw, float length )
	{
		foreach ( var tr in SweepAll( ProbeLift, WorldPosition + Rotation.FromYaw( yaw ).Forward * length ) )
		{
			var entity = Entity( tr.GameObject );
			if ( Smashes ? IsFixed( entity ) : !IsLightProp( entity ) )
				return tr.StartedSolid ? 0 : tr.Fraction;
		}
		return 1;
	}

	/// <summary>Sweeps three quarters of a second ahead: anything solid there makes it brace for impact.</summary>
	void Anticipate()
	{
		if ( SweepAll( ProbeLift, WorldPosition + WorldRotation.Forward * (0.75f * GroundSpeed) ).Any( x => !IsLightProp( Entity( x.GameObject ) ) ) )
			Layer( _anticipation, ref _sinceAnticipation );
	}

	/// <summary>Layers a gesture, letting one that is still playing finish rather than restarting it every think.</summary>
	void Layer( string clip, ref TimeSince since )
	{
		if ( clip is null || since < Duration( clip, 0 ) )
			return;
		PlayGesture( clip );
		since = 0;
	}

	/// <summary>Moves the body by the clip's ground motion since the last think, up and down steps; returns what blocked it.</summary>
	SceneTraceResult? Advance()
	{
		// The clip's motion is in its starting frame, so undo the turn it has made so far, then add this think's turn
		var (from, fromYaw) = Travelled( _lastTime );
		var (to, toYaw) = Travelled( _time );
		var step = (Rotation.FromYaw( WorldRotation.Yaw() - fromYaw ) * (to - from)).WithZ( 0 );
		WorldRotation = Rotation.FromYaw( WorldRotation.Yaw() + toYaw - fromYaw );
		if ( step.Length < 0.01f )
			return null;

		// Light props on the way are ploughed through; the first thing that isn't blocks the step
		var raise = Vector3.Up * StepHeight;
		var tr = default( SceneTraceResult );
		foreach ( var hit in SweepAll( 0, WorldPosition + raise, WorldPosition + raise + step ) )
		{
			if ( !IsLightProp( Entity( hit.GameObject ) ) )
			{
				tr = hit;
				break;
			}
			Plough( Entity( hit.GameObject ) );
		}

		var blocked = tr.Hit || tr.StartedSolid;
		// Running into the enemy closes the gap to it
		if ( blocked && (tr.StartedSolid || Entity( tr.GameObject ) != _target) )
			return tr;

		// It only stands on fixed ground, never on props or characters. No floor short of a ledge's drop: it stays put and
		// pulls up at the edge, or gives up in the wind-up.
		var end = blocked ? tr.EndPosition : WorldPosition + raise + step;
		var floor = SweepAll( 0, end, end - Vector3.Up * (StepHeight + LedgeHeight) ).FirstOrDefault( x => IsFixed( Entity( x.GameObject ) ) );
		if ( !floor.Hit )
		{
			if ( State == Phase.Start )
				Finish();
			else if ( State == Phase.Run )
				Enter( Phase.Stop, _stop );
			return null;
		}
		WorldPosition = floor.EndPosition;
		return blocked ? tr : null;
	}

	/// <summary>The clip's ground motion and turn from its start to <paramref name="time"/>.</summary>
	(Vector3 Position, float Yaw) Travelled( float time ) => _sequences.Travelled( Clip, Duration( Clip, 0 ), time );

	/// <summary>What a step ran into decides it: an enemy is hit, an antlion trampled, a light prop ploughed through, the rest crashed into.</summary>
	void Blocked( SceneTraceResult tr )
	{
		PushAround( tr.Hit && !tr.StartedSolid ? tr.HitPosition : WorldPosition );

		var entity = Entity( tr.GameObject );
		if ( IsFixed( entity ) && EnemyInFront() )
			entity = _target;

		if ( Hates( entity ) )
			Hit( entity );
		else if ( IsCharacter( entity ) )
		{
			// Anyone else only holds it up
			if ( IsAntlion( entity ) )
				Trample( entity );
		}
		else
			Crash();
	}

	/// <summary>A light prop in the way is knocked forward and aside at its pace and passed through for a while; the hunter breaks a weak one.</summary>
	void Plough( GameObject prop )
	{
		PushAround( prop.WorldPosition );
		if ( !Smashes && prop.GetComponent<Component.IDamageable>() is { Health: >= 1 and <= 50 } )
			Strike( prop, NonPlayerDamage, DamageTags.Melee, Npcs.CapturePoint( prop ), default );
		if ( !prop.IsValid() )
			return;
		var side = WorldRotation.Right * MathF.Sign( WorldRotation.Right.Dot( prop.WorldPosition - WorldPosition ) );
		Push( prop, (WorldRotation.Forward + side).Normal * (GroundSpeed * prop.GetComponent<Rigidbody>().Mass) );
		_passing[prop] = 2f;
	}

	/// <summary>The guard's blocked steps push loose props near the block away, at most every 0.1 seconds.</summary>
	void PushAround( Vector3 at )
	{
		if ( !Smashes || _sincePush < 0.1f )
			return;
		_sincePush = 0;
		ImpactPush( at, 128, 350 );
	}

	/// <summary>Stepping up into the enemy can report the world: an enemy close, ahead and in clear view is what it hit.</summary>
	bool EnemyInFront()
	{
		var enemy = Npcs.CapturePoint( _target );
		if ( Centre.Distance( enemy ) > 156 || WorldRotation.Forward.Dot( (enemy - Centre).WithZ( 0 ).Normal ) <= 0.8f )
			return false;
		var tr = Scene.Trace.Ray( Centre, enemy ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		return !tr.Hit || Entity( tr.GameObject ) == _target;
	}

	/// <summary>The blow: damage and a push, and a player is also thrown, punched in the view and flashed red.</summary>
	void Hit( GameObject victim )
	{
		if ( _sinceHit < HitCooldown )
			return;
		_sinceHit = 0;
		PlaySound( HitSound );
		Layer( _hitGesture, ref _sinceHitGesture );
		var player = Npcs.IsPlayer( victim );
		var centre = Npcs.CapturePoint( victim );
		var away = (centre - Centre).Normal;
		var shove = WorldRotation.Forward * 400 + Vector3.Up * 200;
		var scatter = new Vector3( Game.Random.Float( -32, 32 ), Game.Random.Float( -32, 32 ), Game.Random.Float( -32, 32 ) );
		Strike( victim, player ? _skill.Get( DamageSetting, 20 ) : NonPlayerDamage, DamageTags.Melee, centre + scatter, player ? default : away * HitForce );
		if ( player )
			ThrowPlayer( victim, away.WithZ( 0 ).Normal * 250 + Vector3.Up * 128 + shove, new Angles( 20, 20, -30 ), shoved: false );
		else if ( Smashes )
			Npcs.Knockback( victim, shove );
		Enter( Phase.Stop, _stop );
	}

	/// <summary>An antlion in the way dies under it.</summary>
	void Trample( GameObject antlion ) =>
		Strike( antlion, antlion.GetComponent<CreatureNpc>().Health, DamageTags.Melee, Npcs.CapturePoint( antlion ), default );

	/// <summary>Damage with a push behind it, which throws a prop it hits; characters only take the damage.</summary>
	void Strike( GameObject victim, float amount, string tag, Vector3 position, Vector3 force )
	{
		Npc.Damage( victim, Centre, position, tag, amount );
		if ( !force.IsNearlyZero() && victim.IsValid() && !IsCharacter( victim ) )
			Push( victim, force );
	}

	/// <summary>The crash lands its impact at once and holds it until the animation ends; in the wind-up it gives up the charge instead.</summary>
	void Crash()
	{
		if ( State == Phase.Start )
		{
			Finish();
			return;
		}

		// One of the crash sequences, weighted as the model weights them
		var choice = Game.Random.Next( Math.Max( _crashes.Sum( x => Math.Abs( x.ActivityWeight ) ), 1 ) );
		var crash = _crashes.FirstOrDefault( x => (choice -= Math.Abs( x.ActivityWeight )) < 0 ) ?? _crashes.FirstOrDefault();
		Enter( Phase.Crash, crash?.Name ?? _stop );
		Impact();
	}

	/// <summary>The impact: a shake and a thud, and the guard smashes what is in front of it and blasts the props around.</summary>
	void Impact()
	{
		Shake( CrashShakeAmplitude, CrashShakeRadius, 4, 1 );
		PlaySound( CrashSound );
		if ( !Smashes )
			return;

		var end = WorldPosition + WorldRotation.Forward * (new Vector3( _radius, _radius, _height * 0.5f ).Length + 8);
		var struck = new HashSet<GameObject>();
		foreach ( var tr in SweepAll( 2, end ) )
		{
			var entity = Entity( tr.GameObject );
			if ( !entity.IsValid() || !struck.Add( entity ) || entity.GetComponent<Component.IDamageable>() is null )
				continue;

			var character = IsCharacter( entity );
			if ( character && !Hates( entity ) )
				continue;
			var player = Npcs.IsPlayer( entity );
			var amount = player ? _skill.Get( "sk_antlionguard_dmg_shove", 10 ) : NonPlayerDamage;
			var centre = Npcs.CapturePoint( entity );
			Strike( entity, amount, DamageTags.Crush, centre, (centre - Centre).Normal * (amount * 1200) );
			// The first enemy ends the sweep
			if ( character )
				break;
		}

		ImpactPush( end, 200, 500 );
	}

	/// <summary>
	/// Shoves loose physics objects near a point away from its own origin, harder the closer they are, down to half at the edge;
	/// only those within <paramref name="reach"/> of its origin.
	/// </summary>
	internal void ImpactPush( Vector3 centre, float radius, float strength, float reach = float.PositiveInfinity )
	{
		var bodies = Scene.FindInPhysics( new Sphere( centre, radius ) ).Select( x => x.GetComponentInParent<Rigidbody>() )
			.Where( x => x.IsValid() && x.MotionEnabled && !x.Tags.Has( "ragdoll" ) && !IsCharacter( Entity( x.GameObject ) ) ).Distinct().ToArray();
		foreach ( var body in bodies )
		{
			var mass = body.PhysicsBody.MassCenter;
			if ( WorldPosition.Distance( mass ) > reach )
				continue;
			var falloff = 1 - 0.5f * MathF.Min( centre.Distance( mass ) / radius, 1 );
			var impulse = MathF.Max( 1, strength - WorldPosition.Distance( mass ) / 2.5f ) * PushScale * falloff * Game.Random.Float( 0.85f, 1.15f );
			Push( body.GameObject, (mass - WorldPosition).Normal * impulse );
		}
	}

	void Component.IDamageable.IEvents.OnDamaging( Component.IDamageable.DamageEvent e )
	{
		if ( e.Victim == GameObject && State == Phase.Run && (e.DamageInfo.Tags.Contains( DamageTags.Bullet ) || e.DamageInfo.Tags.Contains( DamageTags.Melee )) )
			e.AcceptedDamage *= RunDamageScale;
	}

	/// <summary>An impulse through the body's mass centre, adding at most <see cref="MaxPushSpeed"/>, applied where the body is simulated.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void Push( GameObject target, Vector3 impulse )
	{
		if ( target.IsValid() && target.GetComponent<Rigidbody>() is { IsProxy: false } body )
			body.ApplyImpulse( impulse.ClampLength( MaxPushSpeed * body.Mass ) );
	}

	/// <summary>
	/// A charged or shoved player is thrown, has the view knocked askew and the screen flashes red; a shove adds to the
	/// player's speed rather than setting it, and shakes the screen as hard as a shake may. The owner moves the player.
	/// </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void ThrowPlayer( GameObject victim, Vector3 velocity, Angles punch, bool shoved )
	{
		var player = victim?.GetComponent<PlayerController>();
		if ( !player.IsValid() || player.IsProxy )
			return;

		player.PreventGrounding( 0.2f );
		player.Body.Velocity = shoved ? player.Body.Velocity + velocity : velocity;
		ViewPunch.Apply( player, punch, reset: false );
		if ( shoved )
			player.Scene.Camera?.AddShake( 16, 1.5f, 1 );
		ScreenFade.Show( player.Scene.Camera, new Color( 128 / 255f, 0, 0, 128 / 255f ), 0.1f, 1 );
	}

	/// <summary>Shakes the screen of everyone near it, the amplitude held to the strongest a shake may be.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal void Shake( float amplitude, float radius, float frequency, float duration )
	{
		if ( !Application.IsDedicatedServer )
			Scene.GetSystem<CameraEffectSystem>()?.AddShake( WorldPosition, radius, MathF.Min( amplitude, 16 ), frequency, duration );
	}

	SceneTrace Sweep( float bottom, Vector3 from, Vector3 to )
	{
		var hull = new BBox( new Vector3( -_radius, -_radius, bottom ), new Vector3( _radius, _radius, _height ) );
		var trace = Scene.Trace.Box( hull, from, to ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ragdoll", "debris" );
		foreach ( var prop in _passing.Keys )
			trace = trace.IgnoreGameObjectHierarchy( prop );
		return trace;
	}

	IEnumerable<SceneTraceResult> SweepAll( float bottom, Vector3 to ) => SweepAll( bottom, WorldPosition, to );
	IEnumerable<SceneTraceResult> SweepAll( float bottom, Vector3 from, Vector3 to ) => Sweep( bottom, from, to ).RunAll().OrderBy( x => x.Fraction );

	/// <summary>What a trace hit, as one thing: the character, the physics object or the damageable it belongs to.</summary>
	static GameObject Entity( GameObject hit ) =>
		hit?.GetComponentInParent<BaseNpc>()?.GameObject ?? hit?.GetComponentInParent<PlayerController>()?.GameObject
		?? hit?.GetComponentInParent<Rigidbody>()?.GameObject ?? (hit?.GetComponentInParent<Component.IDamageable>() as Component)?.GameObject;

	static bool IsCharacter( GameObject entity ) => entity.IsValid() && (entity.GetComponent<BaseNpc>().IsValid() || Npcs.IsPlayer( entity ));

	/// <summary>The world, a brush or anything else that is not a character and does not move.</summary>
	static bool IsFixed( GameObject entity ) => !IsCharacter( entity ) && entity?.GetComponent<Rigidbody>() is not { MotionEnabled: true };

	/// <summary>Antlions and workers; guards are not trampled.</summary>
	static bool IsAntlion( GameObject entity ) => entity.IsValid() && entity.GetComponent<CreatureNpc>() is { Voice: "NPC_Antlion", Health: >= 1 };

	bool Hates( GameObject entity ) => entity.IsValid() && !Npcs.IsDead( entity ) && Npc.GetDisposition( entity ) == Disposition.Hostile;

	bool IsLightProp( GameObject entity ) => entity.IsValid() && !IsCharacter( entity )
		&& entity.GetComponent<Rigidbody>() is { MotionEnabled: true } body && MathF.Max( body.Mass, body.MassOverride ) <= _mass * LightPropRatio;
}

/// <summary>Holds a charging creature's body: off the navmesh, out of the ground check and physics, so only the charge moves it.</summary>
[Title( "HL2 Charge Mover" )]
[Category( "Half-Life 2" )]
public sealed class ChargeMover : MovementLayer
{
	public override bool IsMoving => false;
	public override MovementStatus Status => MovementStatus.Idle;
	public override void MoveTo( Vector3 target, float stopDistance = 10f, float? speed = null, GameObject faceTarget = null ) { }
	public override void Stop() { }
}
