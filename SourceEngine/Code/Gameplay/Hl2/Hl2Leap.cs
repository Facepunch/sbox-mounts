using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>
/// npc_headcrab / npc_fastzombie leap attack: the model's ACT_RANGE_ATTACK1 winds up, the jump launches at its
/// leap event on CBaseHeadcrab::JumpAttack's arc, and the bite lands on touch while airborne.
/// </summary>
[Title( "HL2 Leap" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Leap : SourceCreatureBehaviour
{
	/// <summary>Sound script prefix for the attack and bite sounds, such as NPC_Headcrab.</summary>
	[Property] public string SoundPrefix { get; set; }

	/// <summary>The wind-up sound script when it is not &lt;prefix&gt;.Attack, such as NPC_FastZombie.LeapAttack.</summary>
	[Property] public string LeapSound { get; set; }

	/// <summary>HEADCRAB_MIN/MAX_JUMP_DIST and FASTZOMBIE_MINLEAP/MAXLEAP.</summary>
	const float HeadcrabMinJump = 48;
	const float HeadcrabMaxJump = 256;
	const float FastZombieMinLeap = 200;
	const float FastZombieMaxLeap = 300;

	/// <summary>An airborne leap that never lands, stuck on a ledge or in a prop, gives up after this long.</summary>
	const float MaxAirTime = 3;

	/// <summary>How far a crab freed from a dying zombie aims its hop, and how far from the body counts as off it.</summary>
	const float ReleaseHop = 40;
	const float ReleaseClear = 32;

	/// <summary>A hop off the body that has not landed by now is stuck in it, and is tried again.</summary>
	const float ReleaseAirTime = 1;
	const int ReleaseHops = 5;

	[Sync( SyncFlags.FromHost )] bool WindingUp { get; set; }
	[Sync( SyncFlags.FromHost )] bool Airborne { get; set; }
	float _launchTime;
	float _minRange, _maxRange;
	string _airClip;
	bool _poison;
	GameObject _target;
	TimeSince _phase;
	TimeSince _lastLeap = 10;
	bool _bit;
	Flight _flight;
	Vector3 _releasedFrom;
	Vector3 _releaseAway;
	int _releaseHops;
	IReadOnlyDictionary<string, float> _skill = new Dictionary<string, float>();

	protected override async void OnStart() => _skill = await SourceSkillData.Read( SourceMount.Of( GameObject ), "skill_episodic.cfg" );

	public string Clip { get; private set; }
	public override string Animation => WindingUp || Airborne && _airClip is null ? Clip : Airborne ? _airClip : null;
	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(Clip, false), (_airClip, false)];

	protected override void OnConfigure( Model model, SourceModelSequences sequences )
	{
		Clip = sequences.FindActivity( "ACT_RANGE_ATTACK1" );
		_airClip = sequences.FindActivity( "ACT_FASTZOMBIE_LEAP_STRIKE" );
		var launch = sequences.Get( Clip )?.Events.FirstOrDefault( e => e.Name is "AE_HEADCRAB_JUMPATTACK" or "AE_FASTZOMBIE_LEAP" );
		_launchTime = (launch?.Cycle ?? 0) * Duration( Clip, 0 );
		var fastZombie = launch?.Name == "AE_FASTZOMBIE_LEAP";
		_minRange = fastZombie ? FastZombieMinLeap : HeadcrabMinJump;
		_maxRange = fastZombie ? FastZombieMaxLeap : HeadcrabMaxJump;
		_poison = sequences.All.Any( s => s.Activity == "ACT_BLACKHEADCRAB_RUN_PANIC" );
	}

	/// <summary>Host think; true while the leap owns the creature.</summary>
	public override bool Think( GameObject target )
	{
		var npc = Npc;
		if ( !npc.IsValid() || Clip is null )
			return false;

		if ( WindingUp )
		{
			if ( _target.IsValid() )
				WorldRotation = Rotation.LookAt( (_target.WorldPosition - WorldPosition).WithZ( 0 ) );

			if ( _phase >= _launchTime )
				Launch( _target.IsValid() ? AimPoint( _target ) : WorldPosition + WorldRotation.Forward * 128, thrown: false );

			return true;
		}

		if ( Airborne )
		{
			if ( !_bit && _target.IsValid() && Reached( _target ) )
				Bite( _target );

			if ( _phase > 0.2f && npc.IsOnGround || _phase > (_releaseHops > 0 ? ReleaseAirTime : MaxAirTime) )
			{
				Airborne = false;
				Land( npc, _flight );
			}

			return Airborne;
		}

		if ( _releaseHops > 0 )
		{
			// Off the body once it stands on something else, away from where the zombie fell
			var from = (WorldPosition - _releasedFrom).WithZ( 0 );
			if ( npc.IsOnGround && !npc.GroundObject.Tags.Has( "ragdoll" ) && from.Length > ReleaseClear )
			{
				_releaseHops = 0;
				return false;
			}

			// The first hop goes the way it was let go, a later one any way out
			var away = _releaseHops == ReleaseHops ? _releaseAway : Rotation.FromYaw( Game.Random.Float( 0, 360 ) ).Forward;
			_releaseHops--;
			ThrowAt( WorldPosition + away * ReleaseHop );
			return true;
		}

		if ( !target.IsValid() || !npc.IsOnGround || _lastLeap < 1.2f )
			return false;

		var distance = WorldPosition.Distance( target.WorldPosition );
		if ( distance < _minRange || distance > _maxRange )
			return false;

		var eye = WorldPosition + Vector3.Up * npc.AttackHeight;
		var trace = Scene.Trace.Ray( eye, AimPoint( target ) ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
		if ( trace.Hit && trace.GameObject?.Root != target.Root )
			return false;

		npc.Movement?.Stop();
		_target = target;
		_phase = 0;
		WindingUp = true;
		PlaySound( string.IsNullOrEmpty( LeapSound ) ? $"{SoundPrefix}.Attack" : LeapSound );
		return true;
	}

	static Vector3 AimPoint( GameObject target ) => target.GetComponent<PlayerController>() is { } player && player.IsValid()
		? player.EyeTransform.Position
		: SourceNpcs.CapturePoint( target );

	/// <summary>A poison zombie flings this crab at a point, on the higher, faster thrown arc.</summary>
	public void ThrowAt( Vector3 point )
	{
		if ( !Networking.IsHost )
			return;

		_target = null;
		Launch( point, thrown: true );
	}

	/// <summary>
	/// Freed from a dying zombie, whose body falls where the crab would stand: it hops off that way with its first
	/// think, and again while it is still on the body.
	/// </summary>
	public void Release( Vector3 away )
	{
		if ( !Networking.IsHost )
			return;

		_releasedFrom = WorldPosition;
		_releaseAway = away.WithZ( 0 ).Normal;
		_releaseHops = ReleaseHops;
	}

	void Launch( Vector3 point, bool thrown )
	{
		var wasAirborne = Airborne;
		WindingUp = false;
		Airborne = true;
		_bit = false;
		_phase = 0;
		_lastLeap = 0;
		var flight = TakeOff( Npc, JumpVelocity( WorldPosition, point, thrown, Scene.PhysicsWorld.Gravity.Length ) );
		if ( !wasAirborne )
			_flight = flight;
	}

	/// <summary>What <see cref="TakeOff"/> changed, for <see cref="Land"/> to put back.</summary>
	internal readonly record struct Flight( float Damping );

	/// <summary>
	/// Launches the creature on a Source jump arc with no drag. A launch is not a fall, so landing it does no damage.
	/// </summary>
	internal static Flight TakeOff( BaseNpc npc, Vector3 velocity )
	{
		var body = npc.GetComponent<Rigidbody>();
		var flight = new Flight( body.IsValid() ? body.LinearDamping : 0 );
		if ( !body.IsValid() )
			return flight;

		body.LinearDamping = 0;
		npc.Launch( velocity );
		return flight;
	}

	internal static void Land( BaseNpc npc, Flight flight )
	{
		if ( npc.GetComponent<Rigidbody>() is { } body && body.IsValid() )
			body.LinearDamping = flight.Damping;
	}

	/// <summary>CBaseHeadcrab::JumpAttack: rise to the target height (16 to 120, or 400 thrown), overshoot low jumps by 8, cap the speed.</summary>
	static Vector3 JumpVelocity( Vector3 from, Vector3 to, bool thrown, float gravity )
	{
		gravity = MathF.Max( gravity, 1 );
		var height = Math.Clamp( to.z - from.z, 16, thrown ? 400 : 120 );
		var additional = height < 32 ? 8 : 0;
		height += additional;
		var speed = MathF.Sqrt( 2 * gravity * height );
		var time = speed / gravity + MathF.Sqrt( 2 * additional / gravity );
		var velocity = (to - from) / time;
		velocity.z = speed;
		var max = thrown ? 1000 : 650;
		return velocity.Length > max ? velocity * (max / velocity.Length) : velocity;
	}

	/// <summary>The bite lands when the leaper's body, a little wider than its collider, reaches the target's body.</summary>
	bool Reached( GameObject target )
	{
		var collider = GetComponentInChildren<BoxCollider>();
		var reach = (collider.IsValid() ? MathF.Max( collider.Scale.x, collider.Scale.y ) * 0.5f : 12) + 4;
		return SourceNpcs.Touches( GameObject, target, GameObject.GetBounds().Center, reach, out _ );
	}

	internal static bool Touching( GameObject self, GameObject target )
	{
		var center = self.GetBounds().Center;
		var reach = self.GetBounds().Size.Length * 0.5f + 16;
		return target.GetBounds().ClosestPoint( center ).Distance( center ) <= reach;
	}

	void Bite( GameObject target )
	{
		_bit = true;
		var npc = Npc;
		var victim = target.GetComponentInParent<Component.IDamageable>();
		if ( victim is null )
			return;

		PlaySound( $"{SoundPrefix}.Bite" );
		var health = SourceNpcs.HealthOf( target );
		var isNpc = target.GetComponent<BaseNpc>().IsValid();
		var before = health?.Health ?? 0;
		var amount = npc.AttackDamage;
		// CBlackHeadcrab::TouchDamage never kills; it poisons a player down to one point and that heals back.
		if ( _poison && before > 1 )
			amount = MathF.Min( amount, before - 1 );

		var damage = new DamageInfo { Damage = amount, Attacker = GameObject, Weapon = GameObject, Origin = WorldPosition, Position = target.GetBounds().Center };
		damage.Tags.Add( "melee" );
		victim.OnDamage( damage );
		if ( !_poison || health is null || !target.IsValid() )
			return;

		// An NPC takes more poison damage instead of being left on one point
		if ( !isNpc && health.Health > 1 && health.Health < before )
		{
			var poisoned = health.Health - 1;
			health.Health = 1;
			target.GetOrAddComponent<Hl2PoisonRecovery>().Add( poisoned );
		}
		else if ( isNpc && health.Health > 1 )
		{
			var extra = new DamageInfo { Damage = _skill.Get( "sk_headcrab_poison_npc_damage", 20 ), Attacker = GameObject, Weapon = GameObject, Origin = WorldPosition, Position = damage.Position };
			extra.Tags.Add( "melee" );
			victim.OnDamage( extra );
		}
	}
}

/// <summary>Restores health taken by poison, ten points every two seconds like the player's poison recovery.</summary>
[Title( "HL2 Poison Recovery" )]
[Category( "Half-Life 2" )]
public sealed class Hl2PoisonRecovery : Component
{
	float _pending;
	TimeSince _lastRestore;

	public void Add( float amount )
	{
		_pending += amount;
		_lastRestore = 0;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || _lastRestore < 2 )
			return;

		_lastRestore = 0;
		var health = SourceNpcs.HealthOf( GameObject );
		if ( health is null || health.Health <= 0 || _pending <= 0 )
		{
			Destroy();
			return;
		}

		var restore = MathF.Min( 10, _pending );
		_pending -= restore;
		health.Health = MathF.Min( health.MaxHealth, health.Health + restore );
	}
}
