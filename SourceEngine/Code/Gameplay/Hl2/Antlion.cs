using System;
using Sandbox.Npcs;

namespace Source1.Hl2;

/// <summary>Plays the burrow-out activity for an antlion spawned by a burrow maker.</summary>
[Title( "HL2 Unburrow" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2Unburrow", "Hl2Unburrow" )]
public sealed class Unburrow : CreatureBehaviour
{
	[Property] public string Activity { get; set; } = "ACT_ANTLION_BURROW_OUT";
	[Property] public string Sound { get; set; } = "NPC_Antlion.BurrowOut";
	[Sync( SyncFlags.FromHost )] bool Digging { get; set; }
	TimeSince _started;
	bool _done;
	string _clip;

	public override string Animation => Digging ? _clip : null;
	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_clip, false)];

	protected override void OnConfigure( Model model, ModelSequences sequences ) => _clip = sequences.FindActivity( Activity );

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
		if ( Sounds.Sound( Sound, MountContent.Of( GameObject ) ) is { } sound )
			Sandbox.Sound.Play( sound, WorldPosition );
	}
}

/// <summary>npc_antlionguard's shove: CNPC_AntlionGuard::Shove throws whoever its melee hits forward 400 and up 150. A charge hit throws on its own.</summary>
[Title( "HL2 Antlion Guard" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2AntlionGuard", "Hl2AntlionGuard" )]
public sealed class AntlionGuard : Component, Component.IDamageable.IEvents
{
	void IDamageable.IEvents.OnDamaged( IDamageable.DamageEvent e )
	{
		if ( e.DamageInfo.Attacker != GameObject || !e.DamageInfo.Tags.Contains( "melee" ) || GetComponent<Charge>() is { Charging: true } )
			return;

		Npcs.Knockback( e.Victim, WorldRotation.Forward * 400 + Vector3.Up * 150 );
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
[Alias( "Source1.Hl2AntlionJump", "Hl2AntlionJump" )]
public sealed class AntlionJump : CreatureBehaviour
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
	Leap.Flight _flight;
	bool _wings;
	SoundHandle _wingSound;
	IReadOnlyDictionary<string, float> _skill = new Dictionary<string, float>();

	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_windUp, false), (_glide, true), (_land, false)];
	public override string Animation => State switch { Phase.WindUp => _windUp, Phase.Air => _glide ?? _windUp, Phase.Land => _land, _ => null };

	protected override async void OnStart() => _skill = await SkillData.Read( MountContent.Of( GameObject ), "skill_episodic.cfg" );

	protected override void OnConfigure( Model model, ModelSequences sequences )
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
				_flight = Leap.TakeOff( npc, _velocity );
				_nextJump = Game.Random.Int( 2, 6 );
				_airHit = false;
				Enter( Phase.Air );
				return true;

			case Phase.Air:
				if ( !_airHit && _target.IsValid() && Leap.Touching( GameObject, _target ) )
				{
					_airHit = true;
					npc.Damage( _target, WorldPosition, Npcs.CapturePoint( _target ), "melee", _skill.Get( "sk_antlion_air_attack_dmg", 0 ) );
				}
				if ( _phase > 0.2f && npc.IsOnGround || _phase > MaxAirTime )
					Landed();
				return true;

			case Phase.Land:
				npc.Movement?.Stop();
				if ( _phase < Duration( _land, 0 ) )
					return true;
				Leap.Land( npc, _flight );
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
		if ( Npcs.IsPlayer( _target ) && _target.GetBounds().ClosestPoint( WorldPosition ).Distance( WorldPosition ) <= MeleeRange )
			npc.Damage( _target, WorldPosition, Npcs.CapturePoint( _target ), "melee" );
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
		if ( open && !Application.IsDedicatedServer && Sounds.Sound( "NPC_Antlion.WingsOpen", MountContent.Of( GameObject ) ) is { } sound )
			_wingSound = GameObject.PlaySound( sound );
	}

	protected override void OnDestroy() => _wingSound?.Stop();
}
