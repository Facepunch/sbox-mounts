using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace Source1.Hl2;

/// <summary>
/// npc_headcrab / npc_fastzombie leap attack: the model's ACT_RANGE_ATTACK1 winds up, the jump launches at its
/// leap event on a ballistic arc at the enemy, and the first touch in flight bites or claws. A headcrab backs off
/// from an enemy that is too close; a fast zombie only leaps out of its run at an enemy looking its way.
/// </summary>
[Title( "HL2 Leap" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2Leap", "Hl2Leap" )]
public sealed class Leap : CreatureBehaviour, INpcPart, Component.ICollisionListener
{
	public enum Leaper { Headcrab, FastHeadcrab, PoisonHeadcrab, FastZombie }

	[Property] public Leaper Kind { get; set; }

	/// <summary>Sound script prefix for the leap sounds, such as NPC_Headcrab.</summary>
	[Property] public string SoundPrefix { get; set; }

	// Crab leap range between the bodies and facing needed; an enemy's eyes this high need headroom first
	const float CrabMinLeap = 48, CrabMaxLeap = 256, CrabFacing = 0.65f, OverhangHeight = 36;

	// A crab squares up to within about 6 degrees before its leap clip starts; past 45 off it turns with its turn clip
	const float CrabAim = 0.994f, TurnClipAngle = 45;

	/// <summary>A turn clip not kept up this long is gone: something else has the crab.</summary>
	const float StaleTurn = 0.05f;

	// Arc limits: aim height and launch speed, for a leap and a throw (the thrown speed cap is also the fast zombie's);
	// the speed caps hold at 600 gravity and scale with the root of the scene's
	const float MaxAimHeight = 120, MaxThrownHeight = 400, MaxLeapSpeed = 650, MaxThrownSpeed = 1000, CapGravity = 600;

	// Crab timing: bite grace after launch, waits after a miss and after a release, the random pause after a leap
	const float BiteGrace = 0.5f, MissCooldown = 1.2f, ReleaseCooldown = 1, MaxPause = 0.5f, MaxAirTime = 4;

	// Backing away: the run, the short fallback and its least distance, the give-up, the edge probe and the hop off it
	const float RetreatFar = 120, RetreatNear = 96, RetreatLeast = 48, RetreatGiveUp = 5, FirmGround = 12, HopSpeed = 200;

	// Fast zombie: range, height limit, facing, the enemy's view cone it must be in, cooldown from launch
	const float ZombieMinLeap = 200, ZombieMaxLeap = 300, ZombieMaxRise = 128, ZombieFacing = 0.8f, ZombieSeenCone = 0.766f, ZombieCooldown = 2;

	// Fast zombie strike on touch: damage, reach ahead, push on a player and view kick
	const float StrikeDamage = 5, StrikeReach = 50, StrikePush = 500, StrikePunch = 15;

	// A crab freed from a dying zombie hops this far off the body, retrying while still on it
	const float ReleaseHop = 40, ReleaseClear = 32, ReleaseAirTime = 1;
	const int ReleaseHops = 5;

	public enum Phase { None, WindUp, Flight, Retreat }

	[Sync( SyncFlags.FromHost )] Phase State { get; set; }

	/// <summary>The leap clip has played out in flight: a crab goes idle, a fast zombie holds its strike pose.</summary>
	[Sync( SyncFlags.FromHost )] bool ClipDone { get; set; }

	/// <summary>The turn clip a crab plays as it turns in place, or null.</summary>
	[Sync( SyncFlags.FromHost )] string TurnClip { get; set; }

	float _launchTime, _airLimit;
	float? _telegraphTime;
	string _idleClip, _strikeClip;
	GameObject _target;
	Vector3? _locked;
	bool _telegraphed, _armed, _failed, _struck, _screamed;
	TimeSince _phase, _clipStarted, _launched, _sinceTarget;
	TimeUntil _nextLeap, _pause;
	string _turnLeft, _turnRight;
	bool _turned;
	TimeSince _sinceTurn;
	Flight? _flight;
	Vector3 _releasedFrom, _releaseAway;
	int _releaseHops;
	public string Clip { get; private set; }

	bool IsZombie => Kind == Leaper.FastZombie;

	/// <summary>Host: winding up, in the air, backing off or still hopping off a body.</summary>
	internal bool Busy => State != Phase.None || _releaseHops > 0;

	/// <summary>Degrees per second it turns to face its enemy through its leap.</summary>
	float TurnRate => Kind is Leaper.FastHeadcrab or Leaper.FastZombie ? 1200 : 300;

	/// <summary>Degrees per second a crab turns in place: in its idle, and with its turn clip when well off.</summary>
	float IdleTurnRate => Kind == Leaper.FastHeadcrab ? 1200 : 300;
	float TurnClipRate => Kind == Leaper.Headcrab ? 150 : IdleTurnRate;

	public override string Animation => State switch
	{
		Phase.WindUp => Clip,
		Phase.Flight => !ClipDone ? Clip : IsZombie ? _strikeClip ?? Clip : _idleClip,
		_ => TurnClip,
	};

	public override IEnumerable<(string Name, bool Loop)> AnimationChoices =>
		[(Clip, false), (_strikeClip, false), (_idleClip, true), (_turnLeft, true), (_turnRight, true)];

	protected override void OnConfigure( Model model, ModelSequences sequences )
	{
		Clip = sequences.FindActivity( "ACT_RANGE_ATTACK1" );
		_idleClip = sequences.FindActivity( "ACT_IDLE" );
		_strikeClip = sequences.FindActivity( "ACT_FASTZOMBIE_LEAP_STRIKE" );
		_turnLeft = sequences.FindActivity( "ACT_TURN_LEFT" );
		_turnRight = sequences.FindActivity( "ACT_TURN_RIGHT" );
		var events = sequences.Get( Clip )?.Events ?? [];
		var duration = Duration( Clip, 0 );
		_launchTime = (events.FirstOrDefault( e => e.Name is "AE_HEADCRAB_JUMPATTACK" or "AE_FASTZOMBIE_LEAP" )?.Cycle ?? 0) * duration;
		_telegraphTime = events.FirstOrDefault( e => e.Name == "AE_HEADCRAB_JUMP_TELEGRAPH" )?.Cycle * duration;
	}

	/// <summary>Host: a turn clip left over from a think in which another behaviour had the crab is dropped.</summary>
	protected override void OnUpdate()
	{
		if ( Networking.IsHost && TurnClip is not null && _sinceTurn > StaleTurn && !_turned )
			TurnClip = null;
		_turned = false;
	}

	/// <summary>Host think; true while the leap owns the creature.</summary>
	public override bool Think( GameObject target )
	{
		var npc = Npc;
		if ( !npc.IsValid() || Clip is null )
			return false;

		// A fight is over after a while without a target; the next one starts with the full scream
		if ( target.IsValid() )
			_sinceTarget = 0;
		else if ( _sinceTarget > 2 )
			_screamed = false;

		// A crab never rides a creature or player: it hops straight off, unless a hop is already under way
		if ( !IsZombie && OnCreature && (State != Phase.Flight || !ClipDone && _launched > 0.1f) )
		{
			HopAway();
			return true;
		}

		switch ( State )
		{
			case Phase.WindUp: return WindingUp();
			case Phase.Flight: return Flying( target );
			case Phase.Retreat: return Retreating( target );
		}

		if ( _releaseHops > 0 )
			return HopOffBody();

		if ( !IsZombie )
			return Squaring( target );

		if ( !CanPounce( target ) )
			return false;

		StartWindUp( target );
		return true;
	}

	/// <summary>
	/// A crab with its enemy in leaping range and in clear view holds its ground from landing to its next leap: it stands
	/// through the pause after a leap, and through a lockout turns in place only to face its enemy roughly; then it squares
	/// up to within about 6 degrees and leaps. Out of range or out of clear view the creature's chase takes it.
	/// </summary>
	bool Squaring( GameObject target )
	{
		// The turn clip shows only while it is turning in place this think
		_turned = false;
		var owns = Square( target );
		if ( !_turned )
			TurnClip = null;
		return owns;
	}

	bool Square( GameObject target )
	{
		var verdict = Judge( target );
		if ( verdict == Verdict.None )
			return false;

		Npc.Movement?.Stop();
		if ( !_pause )
			return true;

		if ( verdict == Verdict.TooClose )
		{
			StartRetreat( target );
			return true;
		}

		// Locked out of leaping: it only turns to face roughly
		if ( !_nextLeap )
		{
			if ( verdict == Verdict.Turn )
				TurnInPlace( target.WorldPosition );
			return true;
		}

		if ( Facing( target.WorldPosition ) < CrabAim )
		{
			TurnInPlace( target.WorldPosition );
			return true;
		}

		StartWindUp( target );
		return true;
	}

	/// <summary>
	/// A crab's turn in place: in its idle at its idle rate, or, begun more than 45 degrees off, with its turn clip at that
	/// clip's rate the whole way round.
	/// </summary>
	void TurnInPlace( Vector3 point )
	{
		var flat = (point - WorldPosition).WithZ( 0 );
		if ( flat.Length < 1 )
			return;

		var yaw = WorldRotation.Yaw();
		var delta = Angles.NormalizeAngle( Rotation.LookAt( flat ).Yaw() - yaw );
		var going = TurnClip is not null && _sinceTurn < StaleTurn * 4 && (TurnClip == _turnLeft ? delta > 0 : delta < 0);
		var clip = going ? TurnClip : MathF.Abs( delta ) > TurnClipAngle ? delta > 0 ? _turnLeft : _turnRight : null;
		_turned = true;
		_sinceTurn = 0;
		var step = (clip is null ? IdleTurnRate : TurnClipRate) * MathF.Min( Time.Delta, 0.2f );
		TurnClip = clip;
		WorldRotation = Rotation.FromYaw( yaw + Math.Clamp( delta, -step, step ) );
	}

	void Enter( Phase phase )
	{
		State = phase;
		_phase = 0;
	}

	bool Settle()
	{
		Npc.Movement?.Stop();
		Enter( Phase.None );
		return false;
	}

	enum Verdict { None, Leap, TooClose, Turn }

	/// <summary>The crab's leap check: in range, a clear view eye to eye and headroom, then facing.</summary>
	Verdict Judge( GameObject target )
	{
		if ( !target.IsValid() || !Npc.IsOnGround )
			return Verdict.None;

		var distance = Gap( target );
		if ( distance > CrabMaxLeap || distance >= CrabMinLeap && !ClearShot( target ) )
			return Verdict.None;
		if ( distance < CrabMinLeap )
			return Verdict.TooClose;

		// Leaping up needs room: a short sweep toward the enemy's eyes must not bump a ceiling or overhang
		var eyes = EyesOf( target );
		var from = WorldPosition + Vector3.Up;
		if ( eyes.z - WorldPosition.z > OverhangHeight && Trace( Scene.Trace.Box( LocalHull(), from, from + (eyes - from).Normal * 8 ) ) is { Hit: true } tr
			&& tr.GameObject?.Root != target.Root )
			return Verdict.TooClose;

		return Facing( target.WorldPosition ) < CrabFacing ? Verdict.Turn : Verdict.Leap;
	}

	/// <summary>
	/// Too close: back off 120 (sliding along a wall in the way), else to a nearby navigable spot farther from the
	/// enemy on its own side, else at least 48 of 96; leaping as soon as the leap is on.
	/// </summary>
	void StartRetreat( GameObject target )
	{
		var npc = Npc;
		_target = target;
		var away = (WorldPosition - target.WorldPosition).WithZ( 0 ).Normal;
		var lift = Vector3.Up * 8;
		var tr = Sweep( WorldPosition + lift, away * RetreatFar, target );
		var clear = tr.StartedSolid ? 0 : tr.Distance;
		var spot = clear >= RetreatFar ? tr.EndPosition - lift : (Vector3?)null;

		// A wall in the way: the rest of the run along it, as long as the whole 120 is covered
		var slide = (away - tr.Normal * away.Dot( tr.Normal )).WithZ( 0 );
		if ( spot is null && tr.Hit && !tr.StartedSolid && slide.Length > 0.1f
			&& Sweep( tr.EndPosition, slide.Normal * (RetreatFar - clear), target ) is { Hit: false, StartedSolid: false } along )
			spot = along.EndPosition - lift;

		var distance = WorldPosition.Distance( target.WorldPosition );
		for ( var i = 0; spot is null && i < 8; i++ )
		{
			if ( Scene.NavMesh?.GetRandomPoint( WorldPosition, RetreatFar ) is { } nav && nav.Distance( target.WorldPosition ) > distance
				&& (nav - WorldPosition).WithZ( 0 ).Dot( away ) > 0 )
				spot = nav;
		}

		if ( spot is null && clear >= RetreatLeast )
			spot = WorldPosition + away * MathF.Min( clear, RetreatNear );

		if ( spot is { } goal && npc.Movement?.ProjectPoint( goal ) is { } point )
		{
			npc.Movement.MoveTo( point, 8, npc.PaceFor( true ), null );
			Enter( Phase.Retreat );
		}
		else if ( ClearShot( target ) )
		{
			// Nowhere to back off to: go for it from here
			StartWindUp( target );
		}
		else
		{
			RetreatFailed();
		}
	}

	bool Retreating( GameObject target )
	{
		// Free to leap again: it stops backing off and squares up for it
		if ( _nextLeap && Judge( target ) == Verdict.Leap )
		{
			Settle();
			return true;
		}

		var status = Npc.Movement?.Status;
		if ( target != _target || !target.IsValid() || status == MovementLayer.MovementStatus.Arrived )
			return Settle();

		if ( status is null or MovementLayer.MovementStatus.Failed || _phase > RetreatGiveUp || _phase > 0.5f && status == MovementLayer.MovementStatus.Idle )
			RetreatFailed();
		return true;
	}

	/// <summary>A crab perched on an edge hops off it; otherwise it stands a moment and decides again.</summary>
	void RetreatFailed()
	{
		Settle();
		if ( Trace( Scene.Trace.Ray( WorldPosition + Vector3.Up, WorldPosition - Vector3.Up * FirmGround ) ).Hit )
			_nextLeap = MaxPause;
		else
			HopAway();
	}

	/// <summary>Standing on a creature or player, rather than the ground.</summary>
	bool OnCreature => Npc.IsOnGround && Npc.GroundObject?.Root is { } under && under != GameObject.Root
		&& (Npcs.IsPlayer( under ) || under.GetComponent<BaseNpc>().IsValid());

	/// <summary>A hop one of eight ways, 200 out and 200 up, breaking off whatever flight it was in.</summary>
	void HopAway()
	{
		if ( _flight is { } flight )
			Land( Npc, flight );
		_flight = null;
		var direction = Rotation.FromYaw( Game.Random.Int( 0, 7 ) * 45 ).Forward;
		WorldRotation = Rotation.LookAt( direction );
		Hop( direction * HopSpeed + Vector3.Up * HopSpeed, MaxAirTime );
	}

	/// <summary>The released crab's hop off the zombie's body, tried again while it is still on it.</summary>
	bool HopOffBody()
	{
		var npc = Npc;
		if ( npc.IsOnGround && npc.GroundObject?.Tags.Has( "ragdoll" ) != true && (WorldPosition - _releasedFrom).WithZ( 0 ).Length > ReleaseClear )
		{
			_releaseHops = 0;
			return false;
		}

		// The first hop goes the way it was let go, a later one any way out
		var away = _releaseHops-- == ReleaseHops ? _releaseAway : Rotation.FromYaw( Game.Random.Float( 0, 360 ) ).Forward;
		Hop( Arc( WorldPosition + away * ReleaseHop, MaxThrownHeight, MaxThrownSpeed, overshoot: true ), ReleaseAirTime );
		return true;
	}

	/// <summary>A flight that is not an attack: no bite, the leap clip skipped.</summary>
	void Hop( Vector3 velocity, float airLimit )
	{
		Launch( velocity, null );
		_armed = false;
		_airLimit = airLimit;
		ClipDone = true;
	}

	/// <summary>The fast zombie leaps out of its run at an enemy in range, about level, ahead and looking its way.</summary>
	bool CanPounce( GameObject target )
	{
		if ( !target.IsValid() || !Npc.IsOnGround || !_nextLeap || Npc.Movement?.IsMoving != true
			|| MathF.Abs( target.WorldPosition.z - WorldPosition.z ) > ZombieMaxRise )
			return false;

		var distance = Gap( target );
		if ( distance < ZombieMinLeap || distance > ZombieMaxLeap || Facing( target.WorldPosition ) < ZombieFacing )
			return false;

		var player = target.GetComponent<PlayerController>();
		var look = (player.IsValid() ? player.EyeAngles.Forward : target.WorldRotation.Forward).WithZ( 0 ).Normal;
		if ( look.Dot( (WorldPosition - target.WorldPosition).WithZ( 0 ).Normal ) <= ZombieSeenCone )
			return false;

		// The first half of the way there must be open
		var from = WorldHull().Center;
		return !Trace( Scene.Trace.Box( new Vector3( 32 ), from, Vector3.Lerp( from, Npcs.CapturePoint( target ), 0.5f ) ) ).Hit;
	}

	void StartWindUp( GameObject target )
	{
		Npc.Movement?.Stop();
		TurnClip = null;
		_target = target;
		_locked = null;
		_telegraphed = false;
		_clipStarted = 0;
		ClipDone = false;
		Enter( Phase.WindUp );

		if ( IsZombie )
		{
			// The first leap of a fight gets the full scream
			PlaySound( _screamed ? $"{SoundPrefix}.RangeAttack" : $"{SoundPrefix}.Scream" );
			_screamed = true;
		}
	}

	bool WindingUp()
	{
		// A crab squared up before its clip and keeps still through it; its aim is the telegraph's lock
		if ( _target.IsValid() && IsZombie )
			TurnToward( _target.WorldPosition );

		// The telegraph locks the jump onto where the enemy's eyes are now
		if ( !_telegraphed && _clipStarted >= _telegraphTime )
		{
			_telegraphed = true;
			if ( Kind != Leaper.FastHeadcrab )
				PlaySound( Kind == Leaper.PoisonHeadcrab ? $"{SoundPrefix}.Telegraph" : "NPC_HeadCrab.Alert" );
			if ( _target.IsValid() )
				_locked = EyesOf( _target );
		}

		// The classic crab gives up when its prey ducks behind cover, the poison crab only until it has telegraphed;
		// with nothing to aim at, none jumps
		var cautious = Kind == Leaper.Headcrab || Kind == Leaper.PoisonHeadcrab && !_telegraphed;
		var aim = _locked ?? (!_target.IsValid() ? null : IsZombie ? Npcs.CapturePoint( _target ) : EyesOf( _target ));
		if ( aim is null || cautious && _target.IsValid() && !ClearShot( _target ) )
			return Settle();

		if ( _clipStarted < _launchTime )
			return true;

		if ( IsZombie )
			_nextLeap = ZombieCooldown;
		Launch( Arc( aim.Value, MaxAimHeight, IsZombie ? MaxThrownSpeed : MaxLeapSpeed, overshoot: !IsZombie ), IsZombie ? $"{SoundPrefix}.LeapAttack" : $"{SoundPrefix}.Attack" );
		return true;
	}

	/// <summary>Into the air with the bite live; the fast zombie's strike goes live when its wind-up clip ends.</summary>
	void Launch( Vector3 velocity, string sound )
	{
		if ( sound is not null )
			PlaySound( sound );
		Npc.Movement?.Stop();

		// Up a unit first when there is room, so it leaves the ground cleanly
		var lift = Trace( Scene.Trace.Box( LocalHull(), WorldPosition, WorldPosition + Vector3.Up ) );
		if ( !lift.Hit && !lift.StartedSolid )
			WorldPosition += Vector3.Up;

		_flight ??= TakeOff( Npc, velocity );
		_locked = null;
		_launched = 0;
		_airLimit = MaxAirTime;
		_failed = false;
		_struck = false;
		_armed = !IsZombie;
		Enter( Phase.Flight );
	}

	bool Flying( GameObject target )
	{
		var prey = _target.IsValid() ? _target : target;
		if ( Kind == Leaper.FastHeadcrab && prey.IsValid() )
			TurnToward( prey.WorldPosition );

		// The clip ending ends a crab's leap even mid-air, and arms the fast zombie's strike
		if ( !ClipDone && _clipStarted >= Duration( Clip, 0 ) )
		{
			ClipDone = true;
			_armed = IsZombie && !_struck;
			if ( !IsZombie && _failed )
				_nextLeap = MissCooldown;
		}

		// Reaching the prey's body counts as touching it; the physics contact alone can miss a character
		if ( _armed && prey.IsValid() && Npcs.Touches( GameObject, prey, WorldHull().Center, LocalHull().Size.x * 0.5f + 4, out _ ) )
			Touched( prey );

		if ( !(ClipDone && (_launched > 0.1f && Npc.IsOnGround || _struck)) && _launched < _airLimit )
			return true;

		// Down: a crab stands a moment before it decides again
		if ( !IsZombie )
			_pause = Game.Random.Float( 0, MaxPause );
		if ( _flight is { } flight )
			Land( Npc, flight );
		_flight = null;
		_armed = false;
		ClipDone = true;
		return Settle();
	}

	void Component.ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( !IsProxy && State == Phase.Flight && collision.Other.Collider is not { IsTrigger: true } )
			Touched( collision.Other.GameObject );
	}

	/// <summary>The first thing touched in flight decides the leap.</summary>
	void Touched( GameObject other )
	{
		if ( !_armed || !other.IsValid() )
			return;

		if ( IsZombie )
		{
			Strike( _target.IsValid() ? _target : other );
			return;
		}

		var npc = Npc;
		var airborne = !npc.IsOnGround || _launched < 0.1f;
		var victim = other.GetComponentInParent<Component.IDamageable>();
		var victimObject = (victim as Component)?.GameObject ?? other.Root;
		var hostile = npc.GetDisposition( victimObject ) == Disposition.Hostile;

		// Scenery in mid-air is a miss, though a brush right after launch leaves the bite live
		if ( !hostile && airborne )
		{
			_failed = true;
			if ( _launched < BiteGrace )
				return;
		}

		_armed = false;
		if ( hostile && airborne && victim is not null )
		{
			_failed = false;
			Bite( victimObject );
		}
	}

	void Bite( GameObject target )
	{
		var npc = Npc;
		PlaySound( $"{SoundPrefix}.Bite" );
		if ( Kind != Leaper.PoisonHeadcrab )
		{
			npc.Damage( target, WorldPosition, Npcs.CapturePoint( target ), DamageTags.Melee );
			return;
		}

		// The poison bite never kills by itself: it leaves the victim on one point at least, then poisons it
		var health = Npcs.HealthOf( target );
		if ( health is null || health.Health <= 1 )
			return;

		var amount = Npcs.IsPlayer( target ) ? npc.PlayerAttackDamage ?? npc.AttackDamage : npc.AttackDamage;
		npc.Damage( target, WorldPosition, Npcs.CapturePoint( target ), DamageTags.Melee, MathF.Min( amount, health.Health - 1 ) );
		if ( !target.IsValid() || health.Health <= 1 )
			return;

		// Through armour down to one point; a player wins it back over the next seconds
		if ( Npcs.IsPlayer( target ) )
			target.GetOrAddComponent<PoisonRecovery>().Add( health.Health - 1 );
		health.Health = 1;
	}

	/// <summary>The touch that ends the pounce: forward motion stops and both claws swipe ahead.</summary>
	void Strike( GameObject enemy )
	{
		_armed = false;
		_struck = true;
		if ( GetComponent<Rigidbody>() is { } body && body.IsValid() )
			body.Velocity = Vector3.Up * body.Velocity.z;

		// A wall between them spoils the swipe altogether
		var centre = WorldHull().Center;
		if ( enemy.IsValid() && Trace( Scene.Trace.Box( new Vector3( 8 ), centre, Npcs.CapturePoint( enemy ) ).IgnoreDynamic() ).Hit )
			return;

		var npc = Npc;
		var forward = WorldRotation.Forward.WithZ( 0 ).Normal;
		var victim = Trace( Scene.Trace.Box( new Vector3( LocalHull().Size.x ), centre, centre + forward * StrikeReach ) ).GameObject?.Root;
		if ( !victim.IsValid() || !Npcs.IsPlayer( victim ) && !victim.GetComponent<BaseNpc>().IsValid() || npc.GetDisposition( victim ) == Disposition.Friendly )
		{
			PlaySound( $"{SoundPrefix}.AttackMiss" );
			return;
		}

		npc.Damage( victim, centre, Npcs.CapturePoint( victim ), DamageTags.Melee, StrikeDamage );
		PlaySound( $"{SoundPrefix}.AttackHit" );
		if ( !Npcs.IsPlayer( victim ) )
			return;

		Punch( victim, new Angles( StrikePunch, Game.Random.Float( -5, 5 ), Game.Random.Float( -5, 5 ) ) );
		CreatureNpc.KnockbackPlayer( victim, forward * StrikePush );
	}

	/// <summary>Kicks the victim's view; the camera belongs to the player's owner.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void Punch( GameObject victim, Angles punch )
	{
		var player = victim?.GetComponent<PlayerController>();
		if ( player.IsValid() && !player.IsProxy )
			ViewPunch.Apply( player, punch, reset: false );
	}

	/// <summary>A poison zombie flings this crab at a point on the higher, faster thrown arc, its bite live.</summary>
	public void ThrowAt( Vector3 point )
	{
		if ( !Networking.IsHost )
			return;

		_target = null;
		_clipStarted = 0;
		ClipDone = false;
		Launch( Arc( point, MaxThrownHeight, MaxThrownSpeed, overshoot: true ), $"{SoundPrefix}.Attack" );
	}

	/// <summary>Freed from a dying zombie: it hops off the body that way, again while still on it, and cannot leap for a second.</summary>
	public void Release( Vector3 away )
	{
		if ( !Networking.IsHost )
			return;

		_releasedFrom = WorldPosition;
		_releaseAway = away.WithZ( 0 ).Normal;
		_releaseHops = ReleaseHops;
		_nextLeap = ReleaseCooldown;
	}

	/// <summary>A club always kills a crab outright.</summary>
	void INpcPart.OnNpcDamaging( Component.IDamageable.DamageEvent damage )
	{
		var attacker = damage.DamageInfo.Attacker;
		if ( !IsZombie && damage.DamageInfo.Tags.Contains( DamageTags.Melee ) && !(attacker.IsValid() && attacker.Root.GetComponent<BaseNpc>().IsValid()) )
			damage.AcceptedDamage = MathF.Max( damage.AcceptedDamage, Npc.Health );
	}

	SceneTraceResult Trace( SceneTrace trace ) => trace.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();

	/// <summary>Sweeps its body along a move, passing through the enemy.</summary>
	SceneTraceResult Sweep( Vector3 from, Vector3 move, GameObject target ) => Trace( Scene.Trace.Box( LocalHull(), from, from + move ).IgnoreGameObjectHierarchy( target.Root ) );

	void TurnToward( Vector3 point )
	{
		if ( (point - WorldPosition).WithZ( 0 ).Length < 1 )
			return;

		var yaw = WorldRotation.Yaw();
		var step = TurnRate * Time.Delta;
		var delta = Angles.NormalizeAngle( Rotation.LookAt( (point - WorldPosition).WithZ( 0 ) ).Yaw() - yaw );
		WorldRotation = Rotation.FromYaw( yaw + Math.Clamp( delta, -step, step ) );
	}

	/// <summary>How squarely it faces a point in the horizontal plane: 1 dead ahead, 0 at right angles.</summary>
	float Facing( Vector3 point ) => WorldRotation.Forward.WithZ( 0 ).Normal.Dot( (point - WorldPosition).WithZ( 0 ).Normal );

	/// <summary>Distance between the bodies: centre to centre, but vertically only the gap when one is above the other.</summary>
	float Gap( GameObject target )
	{
		var self = WorldHull();
		var other = HullOf( target );
		var delta = other.Center - self.Center;
		delta.z = other.Mins.z > self.Maxs.z ? other.Mins.z - self.Maxs.z : other.Maxs.z < self.Mins.z ? other.Maxs.z - self.Mins.z : 0;
		return delta.Length;
	}

	BBox WorldHull() => HullOf( GameObject );
	BBox LocalHull() => WorldHull().Translate( -WorldPosition );

	/// <summary>A player's standing box, or a creature's collision box.</summary>
	static BBox HullOf( GameObject target )
	{
		var player = target.GetComponent<PlayerController>();
		var radius = player.IsValid() ? new Vector3( player.BodyRadius, player.BodyRadius, 0 ) : default;
		return player.IsValid() ? new BBox( player.WorldPosition - radius, player.WorldPosition + radius.WithZ( player.CurrentHeight ) )
			: target.GetComponentInChildren<BoxCollider>() is { } box && box.IsValid() ? box.GetWorldBounds() : target.GetBounds();
	}

	static Vector3 EyesOf( GameObject target ) => target.GetComponent<PlayerController>() is { } player && player.IsValid() ? player.EyeTransform.Position
		: target.GetComponent<BaseNpc>() is { } npc && npc.IsValid() ? npc.EyePosition
		: target.GetBounds().Center;

	/// <summary>Nothing but the enemy between the crab's eyes (6 forward, 11 up) and the enemy's eyes.</summary>
	bool ClearShot( GameObject target )
	{
		var eye = WorldPosition + WorldRotation.Forward.WithZ( 0 ).Normal * 6 + Vector3.Up * 11;
		var tr = Trace( Scene.Trace.Ray( eye, EyesOf( target ) ).UseHitboxes() );
		return !tr.Hit || tr.GameObject?.Root == target.Root;
	}

	/// <summary>Velocity to a point: rise to its height (16 to the cap), overshoot low jumps by 8, scale down if too fast.</summary>
	Vector3 Arc( Vector3 to, float maxHeight, float maxSpeed, bool overshoot )
	{
		var gravity = MathF.Max( Scene.PhysicsWorld.Gravity.Length, 1 );
		var height = Math.Clamp( to.z - WorldPosition.z, 16, maxHeight );
		var extra = overshoot && height < 32 ? 8 : 0;
		var rise = MathF.Sqrt( 2 * gravity * (height + extra) );
		var velocity = (to - WorldPosition) / (rise / gravity + MathF.Sqrt( 2 * extra / gravity ));
		velocity.z = rise;
		maxSpeed *= MathF.Sqrt( gravity / CapGravity );
		return velocity.Length > maxSpeed ? velocity * (maxSpeed / velocity.Length) : velocity;
	}

	/// <summary>What <see cref="TakeOff"/> changed, for <see cref="Land"/> to put back.</summary>
	internal readonly record struct Flight( float Damping );

	/// <summary>Launches the creature on a ballistic arc with no drag; landing a launch does no damage.</summary>
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

	internal static bool Touching( GameObject self, GameObject target )
	{
		var center = self.GetBounds().Center;
		var reach = self.GetBounds().Size.Length * 0.5f + 16;
		return target.GetBounds().ClosestPoint( center ).Distance( center ) <= reach;
	}
}

/// <summary>Gives back health taken by poison: up to ten points every two seconds for ten steps; a new bite starts over.</summary>
[Title( "HL2 Poison Recovery" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2PoisonRecovery", "Hl2PoisonRecovery" )]
public sealed class PoisonRecovery : Component
{
	float _pending;
	int _steps;
	TimeSince _lastRestore;

	public void Add( float amount )
	{
		_pending += amount;
		_steps = 10;
		_lastRestore = 0;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || _lastRestore < 2 )
			return;

		_lastRestore = 0;
		var health = Npcs.HealthOf( GameObject );
		if ( health is null || health.Health <= 0 || _pending <= 0 || _steps-- <= 0 )
		{
			Destroy();
			return;
		}

		var restore = MathF.Min( 10, _pending );
		_pending -= restore;
		health.Health = MathF.Min( health.MaxHealth, health.Health + restore );
	}
}
