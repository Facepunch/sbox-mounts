using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>Digs out of the ground when spawned, playing the model's burrow-out activity before it does anything else.</summary>
[Title( "HL2 Unburrow" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Unburrow : SourceCreatureBehaviour
{
	[Property] public string Activity { get; set; } = "ACT_ANTLION_BURROW_OUT";
	[Property] public string Sound { get; set; } = "NPC_Antlion.BurrowOut";
	[Sync( SyncFlags.FromHost )] bool Digging { get; set; }
	TimeSince _started;
	bool _done;
	string _clip;

	public override string Animation => Digging ? _clip : null;
	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_clip, false)];

	protected override void OnConfigure( Model model, SourceModelSequences sequences ) => _clip = sequences.FindActivity( Activity );

	/// <summary>Host think; true while still digging out.</summary>
	public override bool Think( GameObject target )
	{
		if ( _done || _clip is null )
			return false;

		if ( !Digging )
		{
			Digging = true;
			_started = 0;
			PlaySound();
		}

		if ( _started < Duration( _clip, 0 ) )
			return true;

		Digging = false;
		_done = true;
		return false;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlaySound()
	{
		if ( SourceSounds.Sound( Sound, SourceMount.Of( GameObject ) ) is { } sound )
			Sandbox.Sound.Play( sound, WorldPosition );
	}
}

/// <summary>
/// The charge of npc_antlionguard and npc_hunter: an enemy 256 units to <see cref="MaxRange"/> away in plain view is run down
/// at the charge loop's ground speed, starting, looping and stopping on the model's *_CHARGE_START/RUN/STOP/CRASH activities.
/// ChargeDamage hurts what it hits (250 to anything but a player) and HandleChargeImpact throws it forward 400 and up 200;
/// running into anything else ends in the crash.
/// </summary>
[Title( "HL2 Charge" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Charge : SourceCreatureBehaviour
{
	const float ChargeMin = 256;
	const float ChargeHitRange = 80;
	const float ChargeDamageNonPlayer = 250;

	/// <summary>The guard's 2048 or HUNTER_CHARGE_MAX.</summary>
	[Property] public float MaxRange { get; set; } = 2048;

	/// <summary>Seconds after a charge before the next, plus up to <see cref="RandomDelay"/> more (hunter_charge_min_delay and two RandomFloat( 0, 2.5 )).</summary>
	[Property] public float Delay { get; set; } = 4;
	[Property] public float RandomDelay { get; set; }

	/// <summary>The odds of charging when it could, rolled once a second (hunter_charge_pct).</summary>
	[Property] public float Chance { get; set; } = 1;

	[Property] public string DamageSetting { get; set; } = "sk_antlionguard_dmg_charge";
	[Property] public string HitSound { get; set; } = "NPC_AntlionGuard.Shove";
	[Property] public string CrashSound { get; set; }

	public enum Phase { None, Start, Run, Stop, Crash }
	[Sync( SyncFlags.FromHost )] Phase State { get; set; }
	public bool Charging => State != Phase.None;
	string _start, _run, _stop, _crash;
	TimeSince _phase;
	TimeUntil _nextCharge;
	TimeSince _lastPath;
	float _chargeSpeed;
	GameObject _target;
	IReadOnlyDictionary<string, float> _skill = new Dictionary<string, float>();

	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_start, false), (_run, true), (_stop, false), (_crash, false)];
	public override string Animation => State switch { Phase.Start => _start, Phase.Run => _run, Phase.Stop => _stop, Phase.Crash => _crash ?? _stop, _ => null };

	protected override async void OnStart() => _skill = await SourceSkillData.Read( SourceMount.Of( GameObject ), "skill_episodic.cfg" );

	protected override void OnConfigure( Model model, SourceModelSequences sequences )
	{
		var start = sequences.All.FirstOrDefault( s => s.Activity?.EndsWith( "_CHARGE_START", StringComparison.Ordinal ) == true )?.Activity;
		var prefix = start?[..^"START".Length];
		_start = sequences.FindActivity( start );
		_run = sequences.FindActivity( prefix + "RUN" );
		_stop = sequences.FindActivity( prefix + "STOP" );
		_crash = sequences.FindActivity( prefix + "CRASH" );
		_chargeSpeed = sequences.Get( _run )?.GroundSpeed ?? 0;
	}

	/// <summary>Host think; true while charging.</summary>
	public override bool Think( GameObject target )
	{
		var npc = Npc;
		if ( _start is null || _run is null )
			return false;

		switch ( State )
		{
			case Phase.None:
				if ( !target.IsValid() || !_nextCharge || !npc.IsOnGround || _chargeSpeed < 1 )
					return false;
				var distance = WorldPosition.WithZ( 0 ).Distance( target.WorldPosition.WithZ( 0 ) );
				if ( distance < ChargeMin || distance > MaxRange )
					return false;
				var eye = WorldPosition + Vector3.Up * 60;
				if ( Scene.Trace.Ray( eye, SourceNpcs.CapturePoint( target ) ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run() is { Hit: true } tr && tr.GameObject?.Root != target.Root )
					return false;
				if ( Game.Random.Float() >= Chance )
				{
					_nextCharge = 1;
					return false;
				}
				_target = target;
				Enter( Phase.Start );
				npc.Movement?.Stop();
				return true;

			case Phase.Start:
				Face();
				if ( _phase >= Duration( _start, 0.5f ) )
					Enter( Phase.Run );
				return true;

			case Phase.Run:
				Face();
				// ChargeLookAhead gives up once the enemy is no longer ahead; the time limit only stops a charge that cannot get there.
				var ahead = (_target.IsValid() ? _target.WorldPosition - WorldPosition : default).WithZ( 0 ).Normal;
				if ( !_target.IsValid() || _phase > 0.5f && WorldRotation.Forward.Dot( ahead ) < 0.25f || _phase > MaxRange / _chargeSpeed + 1 )
				{
					Enter( Phase.Stop );
					return true;
				}
				var forward = WorldRotation.Forward;
				var chest = WorldPosition + Vector3.Up * 40;
				if ( _target.GetBounds().ClosestPoint( chest ).Distance( chest ) < ChargeHitRange )
				{
					var damage = SourceNpcs.IsPlayer( _target ) ? _skill.Get( DamageSetting, 20 ) : ChargeDamageNonPlayer;
					PlaySound( HitSound );
					npc.Damage( _target, WorldPosition, SourceNpcs.CapturePoint( _target ), "melee", damage );
					SourceNpcs.Knockback( _target, forward.WithZ( 0 ).Normal * 400 + Vector3.Up * 200 );
					Enter( Phase.Stop );
					return true;
				}
				var hull = Scene.Trace.Ray( chest, chest + forward * 60 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
				if ( hull.Hit && hull.GameObject?.Root != _target.Root )
				{
					PlaySound( CrashSound );
					Enter( Phase.Crash );
					return true;
				}
				if ( _lastPath > 0.25f )
				{
					_lastPath = 0;
					npc.Movement?.MoveTo( WorldPosition + forward * _chargeSpeed, 0, _chargeSpeed, _target );
				}
				return true;

			case Phase.Stop:
			case Phase.Crash:
				npc.Movement?.Stop();
				if ( _phase < Duration( Animation, 0.5f ) )
					return true;
				State = Phase.None;
				_nextCharge = Delay + Game.Random.Float( 0, RandomDelay );
				return false;
		}
		return false;
	}

	void Enter( Phase phase )
	{
		State = phase;
		_phase = 0;
	}

	void Face()
	{
		if ( _target.IsValid() )
			WorldRotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( (_target.WorldPosition - WorldPosition).WithZ( 0 ) ), Time.Delta * 3 );
	}
}

/// <summary>npc_antlionguard's shove: CNPC_AntlionGuard::Shove throws whoever its melee hits forward 400 and up 150. A charge hit throws on its own.</summary>
[Title( "HL2 Antlion Guard" )]
[Category( "Half-Life 2" )]
public sealed class Hl2AntlionGuard : Component, Component.IDamageable.IEvents
{
	void IDamageable.IEvents.OnDamaged( IDamageable.DamageEvent e )
	{
		if ( e.DamageInfo.Attacker != GameObject || !e.DamageInfo.Tags.Contains( "melee" ) || GetComponent<Hl2Charge>() is { Charging: true } )
			return;

		SourceNpcs.Knockback( e.Victim, WorldRotation.Forward * 400 + Vector3.Up * 150 );
	}
}

/// <summary>
/// npc_antlion's jump, ShouldJump and SCHED_ANTLION_JUMP: toward an enemy whose position 1.25 to 2.5 seconds ahead is over 512 units off,
/// it plays ACT_ANTLION_JUMP_START and leaves the ground at AE_ANTLION_START_JUMP on CAI_MoveProbe::CalcJumpLaunchVelocity's arc to
/// four hull widths past that point, within IsJumpLegal's 512 rise and drop, 1024 reach and 640 apex. It glides with its wings out (bodygroup 1, NPC_Antlion.WingsOpen), hurts the first enemy it
/// flies into (sk_antlion_air_attack_dmg) and lands on ACT_ANTLION_LAND, swiping a player enemy within 100 units.
/// </summary>
[Title( "HL2 Antlion Jump" )]
[Category( "Half-Life 2" )]
public sealed class Hl2AntlionJump : SourceCreatureBehaviour
{
	const float JumpMin = 128;
	const float NearTarget = 512;
	const float MaxRise = 512;
	const float MaxDrop = 512;
	const float MaxDistance = 1024;
	const float MaxHorizontalSpeed = 350;
	const float SourceGravity = 600;
	const float Overshoot = 128;
	const float RetestDistance = 128;
	const float MeleeRange = 100;
	const float MaxAirTime = 4;

	/// <summary>Stands in for the jump links of Source paths: an enemy this far above or below is jumped to from closer than 512.</summary>
	const float UnreachableHeight = 72;

	public enum Phase { None, WindUp, Air, Land }
	[Sync( SyncFlags.FromHost )] Phase State { get; set; }
	string _windUp, _glide, _land;
	float _launchTime;
	TimeSince _phase;
	TimeUntil _nextJump;
	Vector3 _velocity;
	Vector3 _lastAttempt;
	GameObject _target;
	bool _airHit;
	Hl2Leap.Flight _flight;
	bool _wings;
	SoundHandle _wingSound;
	IReadOnlyDictionary<string, float> _skill = new Dictionary<string, float>();

	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_windUp, false), (_glide, true), (_land, false)];
	public override string Animation => State switch { Phase.WindUp => _windUp, Phase.Air => _glide ?? _windUp, Phase.Land => _land, _ => null };

	protected override async void OnStart() => _skill = await SourceSkillData.Read( SourceMount.Of( GameObject ), "skill_episodic.cfg" );

	protected override void OnConfigure( Model model, SourceModelSequences sequences )
	{
		_windUp = sequences.FindActivity( "ACT_ANTLION_JUMP_START" );
		_glide = sequences.FindActivity( "ACT_GLIDE" );
		_land = sequences.FindActivity( "ACT_ANTLION_LAND" );
		var launch = sequences.Get( _windUp )?.Events.FirstOrDefault( e => e.Name == "AE_ANTLION_START_JUMP" );
		_launchTime = (launch?.Cycle ?? 0.5f) * Duration( _windUp, 0 );
	}

	/// <summary>Host think; true from the wind-up until the landing is over.</summary>
	public override bool Think( GameObject target )
	{
		var npc = Npc;
		if ( _windUp is null )
			return false;

		switch ( State )
		{
			case Phase.None:
				if ( !ShouldJump( target ) )
					return false;
				_target = target;
				Enter( Phase.WindUp );
				npc.Movement?.Stop();
				return true;

			case Phase.WindUp:
				WorldRotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( _velocity.WithZ( 0 ) ), Time.Delta * 8 );
				if ( _phase < _launchTime )
					return true;
				_flight = Hl2Leap.TakeOff( npc, _velocity );
				_nextJump = Game.Random.Int( 2, 6 );
				_airHit = false;
				Enter( Phase.Air );
				return true;

			case Phase.Air:
				if ( !_airHit && _target.IsValid() && Hl2Leap.Touching( GameObject, _target ) )
				{
					_airHit = true;
					npc.Damage( _target, WorldPosition, SourceNpcs.CapturePoint( _target ), "melee", _skill.Get( "sk_antlion_air_attack_dmg", 0 ) );
				}
				if ( _phase > 0.2f && npc.IsOnGround || _phase > MaxAirTime )
					Landed();
				return true;

			case Phase.Land:
				npc.Movement?.Stop();
				if ( _phase < Duration( _land, 0 ) )
					return true;
				Hl2Leap.Land( npc, _flight );
				State = Phase.None;
				return false;
		}
		return false;
	}

	void Enter( Phase phase )
	{
		State = phase;
		_phase = 0;
	}

	/// <summary>CheckLanding: wings shut, NPC_Antlion.Land, and a swipe at a player enemy in reach. Landing damage stays off until the land clip ends.</summary>
	void Landed()
	{
		var npc = Npc;
		Enter( Phase.Land );
		PlaySound( "NPC_Antlion.Land" );
		if ( SourceNpcs.IsPlayer( _target ) && _target.GetBounds().ClosestPoint( WorldPosition ).Distance( WorldPosition ) <= MeleeRange )
			npc.Damage( _target, WorldPosition, SourceNpcs.CapturePoint( _target ), "melee" );
	}

	bool ShouldJump( GameObject target )
	{
		if ( !target.IsValid() || !_nextJump || !Npc.IsOnGround )
			return false;

		var dot = MathF.Max( WorldRotation.Forward.Dot( target.WorldRotation.Forward ), 0.5f );
		var predicted = target.WorldPosition + Velocity( target ) * dot * 2.5f;
		var delta = predicted - WorldPosition;
		if ( delta.Length < JumpMin || delta.Length < NearTarget && MathF.Abs( delta.z ) < UnreachableHeight )
			return false;

		if ( _lastAttempt.Distance( predicted ) < RetestDistance )
		{
			_nextJump = Game.Random.Float( 1, 2 );
			return false;
		}

		var land = predicted + delta.WithZ( 0 ).Normal * Overshoot;
		var floor = Scene.Trace.Ray( land + Vector3.Up * 32, land - Vector3.Up * 96 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "player", "npc" ).Run();
		if ( !floor.Hit )
		{
			_nextJump = Game.Random.Float( 1, 2 );
			return false;
		}
		land = floor.EndPosition;
		var from = WorldPosition;
		var gravity = MathF.Max( Scene.PhysicsWorld.Gravity.Length, 1 );
		var velocity = LaunchVelocity( from, land, gravity );
		var apex = velocity.z * velocity.z / (2 * gravity);
		if ( land.z - from.z > MaxRise || from.z - land.z > MaxDrop || apex > MaxRise * 1.25f || from.Distance( land ) > MaxDistance || !Clear( from, land, velocity, gravity, target ) )
		{
			_nextJump = Game.Random.Float( 1, 2 );
			return false;
		}

		_lastAttempt = predicted;
		_velocity = velocity;
		return true;
	}

	static Vector3 Velocity( GameObject target ) => target.GetComponent<PlayerController>() is { } controller && controller.IsValid() ? controller.Velocity
		: target.GetComponent<NavMeshAgent>() is { } agent && agent.IsValid() ? agent.Velocity
		: target.GetComponent<Rigidbody>()?.Velocity ?? Vector3.Zero;

	/// <summary>
	/// CalcJumpLaunchVelocity: high enough to cover the rise and to keep the horizontal speed under GetMaxJumpSpeed. The cap is
	/// scaled from sv_gravity to this scene's gravity, so a jump of a given length rises as high as in Half-Life 2.
	/// </summary>
	static Vector3 LaunchVelocity( Vector3 from, Vector3 to, float gravity )
	{
		var step = to.z - from.z;
		var flat = (to - from).WithZ( 0 );
		var distance = flat.Length;
		var horizontalTime = distance / (MaxHorizontalSpeed * MathF.Sqrt( gravity / SourceGravity ));
		var height = MathF.Max( 0.5f * gravity * (horizontalTime * 0.5f) * (horizontalTime * 0.5f), step );
		var rise = MathF.Sqrt( 2 * height / gravity );
		var fall = MathF.Sqrt( 2 * MathF.Abs( height - step ) / gravity );
		var velocity = flat.Normal * (distance / (rise + fall));
		velocity.z = MathF.Sqrt( 2 * gravity * height );
		return velocity;
	}

	/// <summary>JumpMoveLimit's ten steps along the arc, at body height.</summary>
	bool Clear( Vector3 from, Vector3 to, Vector3 velocity, float gravity, GameObject target )
	{
		var time = (to - from).WithZ( 0 ).Length / MathF.Max( velocity.WithZ( 0 ).Length, 1 );
		var lift = Vector3.Up * GameObject.GetBounds().Size.z * 0.5f;
		var previous = from + lift;
		for ( var i = 1; i <= 10; i++ )
		{
			var t = time * i / 10;
			var point = from + velocity * t + Vector3.Down * (0.5f * gravity * t * t) + lift;
			var tr = Scene.Trace.Ray( previous, point ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			if ( tr.Hit && tr.GameObject?.Root != target.Root )
				return false;
			previous = point;
		}
		return true;
	}

	/// <summary>SetWings: the wings bodygroup and their buzz follow the synced phase on every client.</summary>
	protected override void OnUpdate()
	{
		var open = State == Phase.Air;
		if ( open == _wings )
			return;

		_wings = open;
		Npc.Renderer?.SetBodyGroup( 1, open ? 1 : 0 );
		_wingSound?.Stop( 0.1f );
		_wingSound = null;
		if ( open && !Application.IsDedicatedServer && SourceSounds.Sound( "NPC_Antlion.WingsOpen", SourceMount.Of( GameObject ) ) is { } sound )
			_wingSound = GameObject.PlaySound( sound );
	}

	protected override void OnDestroy() => _wingSound?.Stop();
}
