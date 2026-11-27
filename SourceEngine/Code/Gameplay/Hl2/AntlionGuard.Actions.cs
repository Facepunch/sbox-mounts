using System;
using Sandbox.Navigation;
using Sandbox.Npcs;
using MoveStatus = Sandbox.Npcs.Layers.MovementLayer.MovementStatus;

namespace Source1.Hl2;

/// <summary>
/// The antlion guard's own actions besides the shove and the charge: how it takes damage, its full-body flinch at heavy
/// hits, hitting props at its enemy, what it does when it can't reach its enemy (a frustrated roar, cover, a run toward
/// the enemy, a wander), its limping run when badly hurt, and its death animation before it falls.
/// </summary>
public sealed partial class AntlionGuard : INpcPart, Component.IDamageable.IEvents, IThrownHit
{
	/// <summary>What the guard is busy with, besides chasing and shoving.</summary>
	public enum Doing { None, Flinch, Approach, Turn, Swat, Roar, Cover, Seek, Wander, Wait, Dying }

	// Damage (all from the guard's own rules)
	const float BulletScale = 0.25f;
	const float BuckshotScale = 0.5f;
	const float CrushCap = 100;
	const float CombineBallDamage = 50;
	const float HeavyBlast = 25;
	const float HeavyThrow = 20;
	const float FlinchCooldown = 8;
	const float HeavyShakeAmplitude = 32, HeavyShakeFrequency = 8, HeavyShakeDuration = 0.5f, HeavyShakeRadius = 512;

	// Props
	const float MinPropMass = 8, MaxPropMass = 750, MinPropRadius = 6, MaxPropSpeed = 16;
	const float ChaseCone = 45;
	const float SearchRadius = 350, UnreachableSearchRadius = 3000;
	const float LineTolerance = 240, UnreachableLineTolerance = 480;
	const float HitSpotGap = 32, HitSpotFloor = 48;
	/// <summary>The guard's bounding radius, which sets how far behind a prop it stands.</summary>
	const float BoundingRadius = 75;
	const int FailedMemory = 8;
	const float PropDrift = 360, PropGiveUp = 30;
	const float SwatReach = 256, SwatCone = 55, SwatMiss = 300, ThrowPoseLimit = 45;
	const float LaunchMin = 512, LaunchMax = 1024, LaunchScale = 3, LaunchLift = 350;
	/// <summary>No physics object moves faster than this.</summary>
	const float MaxPhysicsSpeed = 2000;
	const float SwatPushRadius = 150, SwatPushStrength = 250;

	// Out of reach
	const float SeekBeyond = 1200;
	const float WanderRadius = 1024;
	const float RecheckMove = 240;
	/// <summary>Seconds before a spot it found out of reach is tried again anyway.</summary>
	const float RecheckTime = 3;

	/// <summary>Props any guard has claimed, so two don't go for the same one.</summary>
	static readonly HashSet<Rigidbody> Claimed = [];

	[Sync( SyncFlags.FromHost )] public Doing Busy { get; private set; }
	[Sync( SyncFlags.FromHost )] string Playing { get; set; }
	/// <summary>The throw pose: degrees off its nose to the prop it swats.</summary>
	[Sync( SyncFlags.FromHost )] float ThrowYaw { get; set; }

	string _hurtRun, _roar, _swat, _death;
	string[] _physHits = new string[4];
	float _swatEvent, _deathEvent;

	TimeSince _actionTime;
	float _lastTime;
	GameObject _enemy;
	TimeUntil _nextFlinch;
	bool _wantCover;
	bool _firstMilestone;
	(bool Wanted, Vector3 From) _flinch;
	bool _interrupt;

	bool _reachable = true;
	TimeUntil _nextReachCheck;
	Vector3 _unreachableAt;
	TimeSince _sinceUnreachable;
	TimeUntil _nextPropSearch;
	TimeUntil _nextRoar;
	bool _retriedSeek;
	bool _waitToBeSeen;

	Rigidbody _prop;
	Vector3 _propFrom, _hitSpot;
	readonly List<Rigidbody> _failed = [];
	TimeSince _sincePath;

	bool _lastHeavy;

	bool _finishing;
	DamageInfo _deathInfo;

	public override string Animation => Playing;

	public override IEnumerable<(string Name, bool Loop)> AnimationChoices =>
		[(_hurtRun, true), (_roar, false), (_swat, false), (_death, false), .. _physHits.Select( x => (x, false) )];

	protected override void OnConfigure( Model model, ModelSequences sequences )
	{
		_hurtRun = sequences.FindActivity( "ACT_ANTLIONGUARD_RUN_HURT" );
		_roar = sequences.FindActivity( "ACT_ANTLIONGUARD_ROAR" );
		_swat = sequences.FindActivity( "ACT_ANTLIONGUARD_SHOVE_PHYSOBJECT" );
		_death = sequences.FindActivity( "ACT_DIESIMPLE" );
		_physHits = new[] { "FR", "FL", "RR", "RL" }.Select( x => sequences.FindActivity( "ACT_ANTLIONGUARD_PHYSHIT_" + x ) ).ToArray();
		_swatEvent = EventCycle( sequences, _swat, "AE_ANTLIONGUARD_SHOVE_PHYSOBJECT", 0.36f );
		_deathEvent = EventCycle( sequences, _death, "AE_NPC_RAGDOLL", 0.99f );
	}

	static float EventCycle( ModelSequences sequences, string clip, string name, float fallback ) =>
		sequences.Get( clip )?.Events.FirstOrDefault( e => e.Name == name )?.Cycle ?? fallback;

	GuardNavigation Nav => Npc.Movement as GuardNavigation;
	bool Hurt => Npc.IsValid() && Npc.Health >= 1 && Npc.MaxHealth > 0 && Npc.Health <= Npc.MaxHealth * 0.25f;

	/// <summary>Badly hurt, its run is a limp with no sideways steps.</summary>
	public override string TranslateActivity( string activity ) => activity == "ACT_RUN" && Hurt && _hurtRun is not null ? _hurtRun : null;

	// Think

	/// <summary>Host think; true while one of its own actions has the guard.</summary>
	public override bool Think( GameObject target )
	{
		var charge = GetComponent<Charge>();
		if ( Busy == Doing.Dying )
		{
			Carry( _death );
			return true;
		}

		if ( _flinch.Wanted )
		{
			_flinch.Wanted = false;
			charge?.Interrupt();
			StartFlinch( _flinch.From );
			return true;
		}

		if ( _interrupt )
		{
			_interrupt = false;
			charge?.Interrupt();
			End( failed: false );
			if ( _wantCover )
			{
				_wantCover = false;
				// It chooses again: a prop to hit still comes before cover
				if ( target.IsValid() && PropOutOfReach( target ) is { } prop )
					GoForProp( prop, target );
				else
					TakeCover( target );
			}
			return true;
		}

		if ( charge?.Charging == true )
			return false;

		_enemy = target;
		return Busy switch
		{
			Doing.Flinch => Flinching(),
			Doing.Approach => Approaching(),
			Doing.Turn => TurningToSwat(),
			Doing.Swat => Swatting(),
			Doing.Roar => Roaring(),
			Doing.Cover or Doing.Seek or Doing.Wander => Travelling(),
			Doing.Wait => Waiting(),
			_ => Choose( target )
		};
	}

	void Begin( Doing action, string clip = null )
	{
		Busy = action;
		Playing = clip;
		_actionTime = 0;
		_lastTime = 0;
	}

	/// <summary>Back to chasing; a prop it was after is released, and remembered as no good if it failed.</summary>
	bool End( bool failed )
	{
		if ( _prop.IsValid() )
		{
			Claimed.Remove( _prop );
			if ( failed )
				Fail( _prop );
		}
		_prop = null;
		Busy = Doing.None;
		Playing = null;
		ThrowYaw = 0;
		if ( Nav is { } nav )
			nav.FaceTarget = null;
		return false;
	}

	void Fail( Rigidbody prop )
	{
		_failed.Add( prop );
		if ( _failed.Count > FailedMemory )
			_failed.RemoveAt( 0 );
	}

	/// <summary>Nothing of its own going on: once a second it looks for a prop to hit, and deals with an enemy it can't reach.</summary>
	bool Choose( GameObject enemy )
	{
		if ( !enemy.IsValid() || Dead( enemy ) )
		{
			_reachable = true;
			return false;
		}

		if ( _nextReachCheck )
		{
			_nextReachCheck = 1;
			// A spot it found out of reach is tried again after a while, or once the enemy moves well away from it
			if ( _reachable || _sinceUnreachable > RecheckTime || enemy.WorldPosition.Distance( _unreachableAt ) > RecheckMove )
			{
				var reachable = CanReach( enemy );
				_reachable = reachable;
				if ( !reachable )
				{
					_unreachableAt = enemy.WorldPosition;
					_sinceUnreachable = 0;
				}
			}
		}

		if ( !_nextPropSearch )
			return false;
		_nextPropSearch = 1;

		if ( _reachable )
		{
			// On the way to its enemy, a prop roughly in line with it
			if ( Npc.Movement?.IsMoving == true && FindProp( enemy, WorldPosition, chasing: true, 0, LineTolerance ) is { } prop )
				return GoForProp( prop, enemy );
			return false;
		}

		return OutOfReach( enemy );
	}

	/// <summary>
	/// Its enemy can't be reached. In turn: a prop to hit at it, near itself or else near the enemy; a frustrated roar if it
	/// sees the enemy; a run toward a far enemy; else a wander that keeps it in view. Cover comes from heavy damage instead.
	/// </summary>
	bool OutOfReach( GameObject enemy )
	{
		// On a low ledge or crate within reach of its shove, the enemy is shoved instead
		if ( ShoveDistance( enemy ) <= Npc.AttackRange )
			return false;

		if ( PropOutOfReach( enemy ) is { } prop )
			return GoForProp( prop, enemy );

		if ( Sees( enemy ) && _nextRoar && _roar is not null )
		{
			_nextRoar = Game.Random.Float( 20, 40 );
			Npc.Movement?.Stop();
			// It squares up first and roars without turning
			Begin( Doing.Roar );
			return true;
		}

		if ( WorldPosition.Distance( enemy.WorldPosition ) > SeekBeyond && NearestReachable( enemy ) is { } near && near.Distance( WorldPosition ) > 64 )
		{
			_retriedSeek = false;
			Npc.Movement?.MoveTo( near, 48, Npc.PaceFor( true ), enemy );
			if ( Nav is { } nav )
				nav.FaceTarget = enemy;
			Begin( Doing.Seek );
			return true;
		}

		return Wander( enemy );
	}

	/// <summary>
	/// Hit hard while it can't flinch and can't roar at an enemy out of reach that it sees, it runs to a spot out of sight,
	/// an ordinary run, so close to the enemy it backs away facing it.
	/// </summary>
	void TakeCover( GameObject enemy )
	{
		if ( !enemy.IsValid() || CoverSpot( enemy ) is not { } cover )
			return;
		Npc.Movement?.MoveTo( cover, 24, Npc.PaceFor( true ) );
		Begin( Doing.Cover );
	}

	/// <summary>A prop to hit at an enemy it can't reach: near itself, else near the enemy.</summary>
	Rigidbody PropOutOfReach( GameObject enemy ) =>
		FindProp( enemy, WorldPosition, chasing: false, UnreachableSearchRadius, UnreachableLineTolerance )
		?? FindProp( enemy, enemy.WorldPosition, chasing: false, UnreachableSearchRadius, UnreachableLineTolerance );

	/// <summary>Walks to a random spot up to 1024 away, facing its enemy all the way, then waits to be seen.</summary>
	bool Wander( GameObject enemy )
	{
		if ( Scene.NavMesh.GetRandomPoint( WorldPosition, WanderRadius ) is not { } spot )
		{
			Begin( Doing.Wait );
			_waitToBeSeen = false;
			return true;
		}
		Npc.Movement?.MoveTo( spot, 24, Npc.PaceFor( false ), enemy );
		if ( Nav is { } nav )
			nav.FaceTarget = enemy;
		Begin( Doing.Wander );
		return true;
	}

	/// <summary>
	/// Cover, the run toward its enemy and the wander: on until it gets there or can't. Each is dropped as soon as the enemy
	/// is in shove reach or can be reached again; the run also for a charge, the run and the wander for a prop worth hitting.
	/// </summary>
	bool Travelling()
	{
		if ( !_enemy.IsValid() || Npc.Movement is null || _actionTime > 15 )
			return End( false );
		if ( EnemyBack() )
			return End( false );

		if ( Busy == Doing.Seek && GetComponent<Charge>() is { } charge && charge.Think( _enemy ) )
		{
			End( false );
			return true;
		}

		if ( Busy is Doing.Seek or Doing.Wander && _nextPropSearch )
		{
			_nextPropSearch = 1;
			var prop = Busy == Doing.Seek ? FindProp( _enemy, WorldPosition, chasing: true, 0, LineTolerance )
				: FindProp( _enemy, _enemy.WorldPosition, chasing: false, SearchRadius, LineTolerance );
			if ( prop.IsValid() )
			{
				End( false );
				return GoForProp( prop, _enemy );
			}
		}

		// A move just ordered can read as failed until its path is found
		var status = Npc.Movement.Status;
		if ( status == MoveStatus.Failed && _actionTime > 0.5f )
		{
			// The run toward a far enemy gets one more try before it gives up and wanders
			if ( Busy == Doing.Seek && !_retriedSeek && Npc.Movement.MoveTarget is { } goal )
			{
				_retriedSeek = true;
				Npc.Movement.MoveTo( goal, 48, Npc.PaceFor( true ), _enemy );
				return true;
			}
			var wandering = Busy == Doing.Wander;
			End( false );
			if ( !wandering )
				return Wander( _enemy );
			Begin( Doing.Wait );
			_waitToBeSeen = false;
			return true;
		}

		if ( status is MoveStatus.Arrived or MoveStatus.Idle )
		{
			// After cover or the run it chooses again at once; after the wander it waits to be seen
			var wasWander = Busy == Doing.Wander;
			End( false );
			Npc.Movement.Stop();
			_nextReachCheck = 0;
			_nextPropSearch = 0;
			if ( !wasWander )
				return false;
			Begin( Doing.Wait );
			_waitToBeSeen = true;
			return true;
		}
		return true;
	}

	/// <summary>The enemy has stepped into shove reach, or (checked once a second) can be reached again.</summary>
	bool EnemyBack()
	{
		if ( ShoveDistance( _enemy ) <= Npc.AttackRange )
		{
			// The move it ordered goes too, so it doesn't step off toward the old goal first
			Npc.Movement?.Stop();
			return true;
		}
		if ( !_nextReachCheck )
			return false;
		_nextReachCheck = 1;
		if ( !CanReach( _enemy ) )
			return false;
		_reachable = true;
		Npc.Movement?.Stop();
		return true;
	}

	/// <summary>Stands facing its enemy after a wander until the enemy sees it, at most 10 seconds, unless the enemy comes back. Then it chooses again.</summary>
	bool Waiting()
	{
		if ( !_enemy.IsValid() || EnemyBack() )
			return End( false );

		Nav?.Turn( YawTo( _enemy.WorldPosition ), StandingTurnRate );
		var limit = _waitToBeSeen ? 10 : 5;
		if ( _actionTime < 0.5f || _actionTime < limit && !(_waitToBeSeen && Sees( _enemy )) )
			return true;
		_nextReachCheck = 0;
		return End( false );
	}

	/// <summary>Centre to centre, height counting only for the gap where the two bodies don't overlap.</summary>
	float ShoveDistance( GameObject enemy )
	{
		var bounds = enemy.GetBounds();
		var top = WorldPosition.z + (Npc.Centre.z - WorldPosition.z) * 2;
		var gap = MathF.Max( 0, MathF.Max( bounds.Mins.z - top, WorldPosition.z - bounds.Maxs.z ) );
		var flat = (Npcs.CapturePoint( enemy ) - Npc.Centre).WithZ( 0 ).Length;
		return MathF.Sqrt( flat * flat + gap * gap );
	}

	/// <summary>Turning on the spot: 200 degrees a second, 160 against an enemy that isn't a player.</summary>
	float StandingTurnRate => _enemy.IsValid() && !Npcs.IsPlayer( _enemy ) ? Nav?.NpcTurnRate ?? 160 : Nav?.TurnRate ?? 200;

	/// <summary>Squares up to its enemy, then roars without turning.</summary>
	bool Roaring()
	{
		if ( Playing is null )
		{
			var left = _enemy.IsValid() ? Nav?.Turn( YawTo( _enemy.WorldPosition ), StandingTurnRate ) ?? 0 : 0;
			if ( left > 3 && _actionTime < 1.5f )
				return true;
			Begin( Doing.Roar, _roar );
			return true;
		}
		return _actionTime < Duration( _roar, 4.7f ) || End( false );
	}

	// Flinches

	/// <summary>
	/// Stops and reels from where the hit landed, carried by the clip's motion; nothing cuts it short. A hit on its front
	/// right plays the front-left reel and one on its front left the front-right reel; hits behind play the reel of their side.
	/// </summary>
	void StartFlinch( Vector3 at )
	{
		var toHit = (at - Npc.Centre).WithZ( 0 ).Normal;
		var front = WorldRotation.Forward.WithZ( 0 ).Normal.Dot( toHit ) >= 0;
		var right = WorldRotation.Right.WithZ( 0 ).Normal.Dot( toHit ) >= 0;
		// _physHits: FR, FL, RR, RL
		var clip = _physHits[front ? (right ? 1 : 0) : (right ? 2 : 3)];
		_nextFlinch = FlinchCooldown;
		if ( clip is null )
			return;

		End( failed: _prop.IsValid() );
		Npc.Movement?.Stop();
		Begin( Doing.Flinch, clip );
	}

	bool Flinching()
	{
		Carry( Playing );
		return _actionTime < Duration( Playing, 2.4f ) || End( false );
	}

	/// <summary>Moves the standing body by the clip's ground motion and turn since the last think.</summary>
	void Carry( string clip )
	{
		var time = (float)_actionTime;
		var (from, fromYaw) = Travelled( clip, _lastTime );
		var (to, toYaw) = Travelled( clip, time );
		_lastTime = time;
		var step = Rotation.FromYaw( WorldRotation.Yaw() - fromYaw ) * (to - from);
		WorldRotation = Rotation.FromYaw( WorldRotation.Yaw() + toYaw - fromYaw );
		Nav?.Displace( step );
	}

	/// <summary>A clip's ground motion and turn from its start to a time, in its starting frame, along its path where it varies.</summary>
	(Vector3 Position, float Yaw) Travelled( string clip, float time )
	{
		var duration = Duration( clip, 0 );
		if ( ModelSequences.Of( ConfiguredModel ).Get( clip ) is not { } sequence || duration <= 0 )
			return default;

		var cycle = Math.Clamp( time / duration, 0, 1 );
		if ( sequence.MovementPath is not { Length: > 1 } path )
			return (sequence.Movement * cycle, 0);

		var position = cycle * (path.Length - 1);
		var index = Math.Min( (int)position, path.Length - 2 );
		var t = position - index;
		var yaw = sequence.MovementYaw is { Length: > 1 } turn ? MathX.Lerp( turn[index], turn[index + 1], t ) : 0;
		return (Vector3.Lerp( path[index], path[index + 1], t ), yaw);
	}

	// Props

	/// <summary>
	/// The cheapest prop worth hitting at its enemy. Chasing, one closer than the enemy and within 45 degrees of the line to it,
	/// costing more toward the cone's edge; otherwise the nearest to <paramref name="centre"/> within the radius. Either way it can
	/// see it, the prop sits still, a clear line from it ends near the enemy, and there is room to stand behind it.
	/// </summary>
	Rigidbody FindProp( GameObject enemy, Vector3 centre, bool chasing, float radius, float tolerance )
	{
		var me = WorldPosition;
		var foe = enemy.WorldPosition;
		var enemyDistance = (foe - me).WithZ( 0 ).Length;
		var reach = chasing ? enemyDistance : radius;
		if ( reach < 1 )
			return null;

		Rigidbody best = null;
		var bestCost = chasing ? enemyDistance * enemyDistance : float.MaxValue;
		var examined = new HashSet<Rigidbody>();
		foreach ( var go in Scene.FindInPhysics( new Sphere( chasing ? me : centre, reach ) ) )
		{
			var body = go.GetComponentInParent<Rigidbody>();
			if ( !body.IsValid() || !examined.Add( body ) || examined.Count > 200 || !IsProp( body ) || _failed.Contains( body ) || Claimed.Contains( body ) )
				continue;

			var bounds = body.GetWorldBounds();
			var c = bounds.Center;
			float cost;
			if ( chasing )
			{
				var toProp = (c - me).WithZ( 0 );
				var angle = Vector3.GetAngle( toProp, (foe - me).WithZ( 0 ) );
				if ( toProp.Length >= enemyDistance || angle > ChaseCone )
					continue;
				// 1 dead ahead to 4 at the cone's edge, in the cosine of the angle
				var edge = 1 - MathF.Cos( ChaseCone * MathF.PI / 180 );
				cost = toProp.LengthSquared * (1 + 3 * (1 - MathF.Cos( angle * MathF.PI / 180 )) / edge);
			}
			else
			{
				cost = (c - centre).LengthSquared;
				if ( cost > radius * radius )
					continue;
			}

			if ( cost >= bestCost || !Visible( Npc.EyePosition, c, body.GameObject ) || !LinesUp( c, enemy, tolerance ) || HitSpot( body, foe ) is null )
				continue;
			best = body;
			bestCost = cost;
		}
		return best;
	}

	bool IsProp( Rigidbody body )
	{
		if ( body.GameObject == GameObject || !body.MotionEnabled || body.PhysicsBody is not { IsValid: true } )
			return false;
		var tags = body.GameObject.Tags;
		if ( tags.Has( "debris" ) || tags.Has( "ragdoll" ) || tags.Has( "npc" ) || tags.Has( "player" ) || tags.Has( "projectile" ) || tags.Has( ZombieProps.DebrisTag )
			|| body.GetComponentInParent<BaseNpc>().IsValid() || body.GetComponentInParent<PlayerController>().IsValid()
			|| body.GetComponentInParent<ModelPhysics>().IsValid() || body.GetComponentInParent<BaseCombatWeapon>().IsValid() )
			return false;
		var mass = MathF.Max( body.Mass, body.MassOverride );
		return mass is >= MinPropMass and <= MaxPropMass && body.Velocity.Length < MaxPropSpeed && body.GetWorldBounds().Size.Length * 0.5f >= MinPropRadius;
	}

	/// <summary>A clear line through the world from the prop toward the enemy ends close enough to the enemy.</summary>
	bool LinesUp( Vector3 from, GameObject enemy, float tolerance )
	{
		var aim = Npcs.CapturePoint( enemy );
		var tr = Scene.Trace.Ray( from, aim ).IgnoreDynamic().WithoutTags( "trigger", "player", "npc", "playercontroller", "ragdoll", "debris" ).Run();
		return tr.EndPosition.Distance( aim ) <= tolerance;
	}

	bool Visible( Vector3 from, Vector3 to, GameObject seen )
	{
		var tr = Scene.Trace.Ray( from, to ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "debris" ).Run();
		return !tr.Hit || tr.GameObject.IsValid() && (tr.GameObject == seen || seen.IsDescendant( tr.GameObject ) || tr.GameObject.IsDescendant( seen ));
	}

	/// <summary>
	/// Where it stands to hit a prop at the enemy: on the far side of the prop from the enemy, the prop's extent plus its own
	/// bounding radius plus 32 out, or 45 degrees round either way; its body, dropped from 48 above the prop's middle, has to
	/// land on floor within 48 below it.
	/// </summary>
	Vector3? HitSpot( Rigidbody prop, Vector3 enemy )
	{
		var agent = GetComponent<NavMeshAgent>();
		var radius = agent?.Radius ?? 45;
		var height = agent?.Height ?? 130;
		var bounds = prop.GetWorldBounds();
		var centre = bounds.Center;
		var half = bounds.Size * 0.5f;
		var away = (centre - enemy).WithZ( 0 ).Normal;
		if ( away.IsNearlyZero() )
			return null;

		foreach ( var yaw in new[] { 0f, 45, -45 } )
		{
			var direction = Rotation.FromYaw( yaw ) * away;
			var extent = MathF.Abs( direction.x ) * half.x + MathF.Abs( direction.y ) * half.y;
			var spot = centre + direction * (extent + BoundingRadius + HitSpotGap);
			var hull = new BBox( new Vector3( -radius, -radius, 0 ), new Vector3( radius, radius, height ) );
			var floor = Scene.Trace.Box( hull, spot + Vector3.Up * HitSpotFloor, spot - Vector3.Up * HitSpotFloor ).IgnoreGameObjectHierarchy( GameObject )
				.IgnoreGameObjectHierarchy( prop.GameObject ).WithoutTags( "trigger", "player", "npc", "playercontroller", "ragdoll", "debris" ).Run();
			if ( !floor.Hit || floor.StartedSolid )
				continue;
			if ( Scene.NavMesh.GetClosestPoint( floor.EndPosition, 32 ) is { } onMesh && onMesh.WithZ( 0 ).Distance( floor.EndPosition.WithZ( 0 ) ) < 24 )
				return onMesh;
		}
		return null;
	}

	bool GoForProp( Rigidbody prop, GameObject enemy )
	{
		if ( HitSpot( prop, enemy.WorldPosition ) is not { } spot )
			return false;
		_prop = prop;
		_propFrom = prop.WorldPosition;
		_hitSpot = spot;
		Claimed.Add( prop );
		Npc.Movement?.MoveTo( spot, 16, Npc.PaceFor( true ) );
		_sincePath = 0;
		Begin( Doing.Approach );
		return true;
	}

	/// <summary>A prop it went for was moved far off, took too long, or vanished: it gives up on it.</summary>
	bool PropLost() => !_prop.IsValid() || !_enemy.IsValid() || _prop.WorldPosition.Distance( _propFrom ) > PropDrift || _actionTime > PropGiveUp;

	bool Approaching()
	{
		if ( PropLost() )
			return End( failed: true );

		var arrived = (WorldPosition - _hitSpot).WithZ( 0 ).Length <= 24 || Npc.Movement?.Status == MoveStatus.Arrived;
		if ( Npc.Movement?.Status == MoveStatus.Failed && _actionTime > 0.5f )
			return End( failed: true );
		if ( !arrived )
		{
			if ( _sincePath > 1 )
			{
				_sincePath = 0;
				Npc.Movement?.MoveTo( _hitSpot, 16, Npc.PaceFor( true ) );
			}
			return true;
		}

		Npc.Movement?.Stop();
		Begin( Doing.Turn );
		return true;
	}

	/// <summary>At its spot it turns to face the enemy, then needs the prop close and in front to swat it.</summary>
	bool TurningToSwat()
	{
		if ( PropLost() )
			return End( failed: true );

		if ( Nav?.Turn( YawTo( _enemy.WorldPosition ), StandingTurnRate ) > 3 && _actionTime < 1.5f )
			return true;

		var centre = Npc.Centre;
		var prop = _prop.GetWorldBounds().Center;
		var off = Angles.NormalizeAngle( YawTo( prop ) - WorldRotation.Yaw() );
		if ( prop.Distance( centre ) > SwatReach || MathF.Abs( off ) > SwatCone || _swat is null )
			return End( failed: true );

		ThrowYaw = Math.Clamp( off, -ThrowPoseLimit, ThrowPoseLimit );
		Begin( Doing.Swat, _swat );
		return true;
	}

	bool Swatting()
	{
		var at = _swatEvent * Duration( _swat, 1.4f );
		if ( _lastTime < at && _actionTime >= at )
			Launch();
		_lastTime = _actionTime;
		return _actionTime < Duration( _swat, 1.4f ) || End( false );
	}

	/// <summary>
	/// The swat: the prop flies at the enemy's feet, fast and nearly flat, spinning, with a thud and a push to the props around
	/// it. A prop that has rolled off out of reach is missed.
	/// </summary>
	void Launch()
	{
		if ( !_prop.IsValid() || !_enemy.IsValid() )
		{
			GetComponent<Charge>()?.ImpactPush( WorldPosition, SwatPushRadius, SwatPushStrength );
			return;
		}

		var prop = _prop;
		var centre = prop.GetWorldBounds().Center;
		if ( centre.Distance( Npc.Centre ) > SwatMiss )
		{
			Claimed.Remove( prop );
			Fail( prop );
			_prop = null;
			return;
		}

		var feet = _enemy.WorldPosition;
		var distance = centre.Distance( feet );
		var velocity = ((feet - centre).Normal * (Math.Clamp( distance, LaunchMin, LaunchMax ) * LaunchScale) + Vector3.Up * LaunchLift).ClampLength( MaxPhysicsSpeed );
		PlaySound( "NPC_AntlionGuard.HitHard" );
		GetComponent<Charge>()?.ImpactPush( centre, SwatPushRadius, SwatPushStrength );
		// Now it is the guard's throw, and doesn't hurt it if it bounces back
		PlayerThrows.Forget( prop.GameObject );
		prop.Sleeping = false;
		prop.Velocity = velocity;
		var spin = new Vector3( Game.Random.Float( -180, 180 ), 100, Game.Random.Float( -360, 360 ) ) * (MathF.PI / 180);
		prop.AngularVelocity = prop.WorldRotation * spin;

		Claimed.Remove( prop );
		_failed.Clear();
		_prop = null;
	}

	// Out of reach

	/// <summary>Whether a path on the navmesh runs all the way to its enemy's feet.</summary>
	bool CanReach( GameObject enemy )
	{
		var feet = enemy.WorldPosition;
		if ( Scene.NavMesh.GetClosestPoint( feet, 96 ) is not { } ground || ground.WithZ( 0 ).Distance( feet.WithZ( 0 ) ) > 48 || MathF.Abs( ground.z - feet.z ) > 72 )
			return false;
		var path = Scene.NavMesh.CalculatePath( new CalculatePathRequest { Start = WorldPosition, Target = ground, Agent = GetComponent<NavMeshAgent>() } );
		return path.Status == NavMeshPathStatus.Complete && path.Points.Count > 0 && path.Points[path.Points.Count - 1].Position.Distance( ground ) < 48;
	}

	/// <summary>The end of the best path toward its enemy: as near as it can get.</summary>
	Vector3? NearestReachable( GameObject enemy )
	{
		var goal = Scene.NavMesh.GetClosestPoint( enemy.WorldPosition, SeekBeyond ) ?? enemy.WorldPosition;
		var path = Scene.NavMesh.CalculatePath( new CalculatePathRequest { Start = WorldPosition, Target = goal, Agent = GetComponent<NavMeshAgent>() } );
		return path.IsValid && path.Points.Count > 0 ? path.Points[path.Points.Count - 1].Position : null;
	}

	bool Sees( GameObject enemy ) => Npc.Senses?.VisibleHostiles.Contains( enemy ) == true;

	/// <summary>The nearest spot on the navmesh within 800 units that the enemy can't see.</summary>
	Vector3? CoverSpot( GameObject enemy )
	{
		var eye = Npcs.CapturePoint( enemy );
		Vector3? best = null;
		var bestDistance = float.MaxValue;
		for ( var i = 0; i < 24; i++ )
		{
			if ( Scene.NavMesh.GetRandomPoint( WorldPosition, 800 ) is not { } spot )
				continue;
			var distance = spot.Distance( WorldPosition );
			if ( distance >= bestDistance || distance < 128 )
				continue;
			var tr = Scene.Trace.Ray( eye, spot + Vector3.Up * 64 ).IgnoreDynamic().WithoutTags( "trigger", "player", "npc", "playercontroller" ).Run();
			if ( !tr.Hit )
				continue;
			best = spot;
			bestDistance = distance;
		}
		return best;
	}

	float YawTo( Vector3 point ) => Rotation.LookAt( (point - WorldPosition).WithZ( 0 ) ).Yaw();

	static bool Dead( GameObject go ) => !go.IsValid() || Npcs.HealthOf( go ) is { Health: < 1 };

	// Damage

	/// <summary>
	/// Bullets do a quarter (buckshot half), never under 1; another guard does nothing; a physics hit only counts from an object
	/// a player launched, by the guard's own impact table (see <see cref="IThrownHit.OnThrownHit"/>), or from a vehicle, at
	/// most 100; a combine ball does 50. The gravity gun punting the guard itself does nothing.
	/// </summary>
	void INpcPart.OnNpcDamaging( Component.IDamageable.DamageEvent e )
	{
		if ( _finishing )
			return;
		if ( Busy == Doing.Dying )
		{
			e.Cancelled = true;
			return;
		}

		var info = e.DamageInfo;
		var tags = info.Tags;
		var raw = e.AcceptedDamage;
		_lastHeavy = false;

		if ( info.Attacker.IsValid() && info.Attacker.GetComponentInParent<AntlionGuard>() is { } other && other != this )
		{
			e.Cancelled = true;
			return;
		}

		if ( tags.Contains( "dissolve" ) )
		{
			e.AcceptedDamage = CombineBallDamage;
			_lastHeavy = true;
		}
		else if ( (tags.Contains( DamageTags.Crush ) || tags.Contains( "impact" )) && !tags.Contains( "vehicle" ) )
		{
			// Props that fall on it, that it knocked about itself, or that a player only dropped do nothing, nor does a punt
			if ( !tags.Contains( TableTag ) )
			{
				e.Cancelled = true;
				return;
			}
			e.AcceptedDamage = MathF.Min( e.AcceptedDamage, CrushCap );
			_lastHeavy = e.AcceptedDamage >= HeavyThrow && Npc.Health >= Npc.MaxHealth * 0.25f;
		}
		else if ( tags.Contains( DamageTags.Bullet ) )
		{
			e.AcceptedDamage = MathF.Max( 1, raw * (tags.Contains( "buckshot" ) ? BuckshotScale : BulletScale) );
		}
		else if ( tags.Contains( DamageTags.Explosion ) || tags.Contains( "blast" ) )
		{
			_lastHeavy = raw > HeavyBlast;
		}

		if ( e.AcceptedDamage <= 0 )
			e.Cancelled = true;
	}

	/// <summary>
	/// Heavy damage shakes the screen and makes it roar in pain, and flinches it unless it flinched in the last 8 seconds; then
	/// it only drops what it was doing. Bullets never count as heavy, but its first drop below 95 % and every fifth of its health
	/// they take make it flinch and roar too.
	/// </summary>
	void INpcPart.OnNpcHurt( Component.IDamageable.DamageEvent e )
	{
		if ( _finishing || Npc.Health < 1 )
			return;

		var tags = e.DamageInfo.Tags;
		var max = MathF.Max( Npc.MaxHealth, 1 );
		var after = Npc.Health;
		var before = after + e.AcceptedDamage;
		var milestone = false;
		if ( tags.Contains( DamageTags.Bullet ) && !_lastHeavy )
		{
			if ( !_firstMilestone && after < max * 0.95f )
			{
				_firstMilestone = true;
				milestone = after > max * 0.9f;
			}
			for ( var fifth = 4; fifth >= 1 && !milestone; fifth-- )
				milestone = before > max * fifth / 5 && after <= max * fifth / 5;
		}

		if ( !_lastHeavy && !milestone )
			return;

		// Where the hit landed decides the reel; a hit reported at its middle (a blast) by where it came from
		var info = e.DamageInfo;
		var at = !info.Position.IsNearlyZero() && (info.Position - Npc.Centre).WithZ( 0 ).Length > 8 ? info.Position
			: !info.Origin.IsNearlyZero() ? info.Origin : e.Instigator.IsValid() ? e.Instigator.WorldPosition : WorldPosition + WorldRotation.Forward;
		if ( _lastHeavy )
		{
			HeavyShake();
			if ( tags.Contains( DamageTags.Crush ) || tags.Contains( "impact" ) )
				PlaySound( "NPC_AntlionGuard.ShellCrack" );
		}
		PlaySound( "NPC_AntlionGuard.Pain_Roar" );

		if ( Busy == Doing.Flinch )
			return;
		if ( _nextFlinch )
		{
			_flinch = (true, at);
		}
		else
		{
			_interrupt = true;
			// Hit hard by an enemy it can't reach, sees, and can't roar at yet: it runs for cover
			_wantCover = _lastHeavy && !_reachable && _enemy.IsValid() && Sees( _enemy ) && !(_nextRoar && _roar is not null);
		}
	}

	const string TableTag = "guard_impact";

	/// <summary>
	/// The guard's impact table. It reads the energy the object lost in the hit as a speed of the guard's own mass: the speed
	/// it lost, squared, times its mass over the guard's, doubled from 45 kg up. 100 of that does 10, 250 does 25, 350 does
	/// 50, 500 does 75 and 1000 does 100. Nothing from objects under 15 kg or slower than 200 before the hit.
	/// </summary>
	static float ImpactDamage( float before, float after, float mass, float guardMass )
	{
		if ( mass < 15 || before < 200 || guardMass <= 0 )
			return 0;
		var lost = MathF.Max( 0, before - after );
		var speed = MathF.Sqrt( lost * lost * mass / guardMass * (mass >= 45 ? 2 : 1) );
		return speed >= 1000 ? 100 : speed >= 500 ? 75 : speed >= 350 ? 50 : speed >= 250 ? 25 : speed >= 100 ? 10 : 0;
	}

	/// <summary>The guard's mass: its model's, else 1000 kg.</summary>
	float GuardMass => Npc.Renderer?.Model?.Physics?.Parts.Sum( x => x.Mass ) is > 0 and var mass ? mass : 1000;

	/// <summary>An object a player launched strikes it before hitting anything else: damage by the guard's table.</summary>
	void IThrownHit.OnThrownHit( GameObject thing, float mass, GameObject thrower, float speedBefore, float speedAfter, Vector3 point )
	{
		if ( !Networking.IsHost || Busy == Doing.Dying || !Npc.IsValid() || Npc.Health < 1 )
			return;

		var amount = ImpactDamage( speedBefore, speedAfter, mass, GuardMass );
		if ( amount <= 0 )
			return;

		var info = new DamageInfo( amount, thrower.IsValid() ? thrower : thing, thing ) { Position = point, Origin = thing.IsValid() ? thing.WorldPosition : point };
		info.Tags.Add( DamageTags.Crush );
		info.Tags.Add( "impact" );
		info.Tags.Add( TableTag );
		((Component.IDamageable)Npc).OnDamage( info );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void HeavyShake()
	{
		if ( !Application.IsDedicatedServer )
			Scene.GetSystem<CameraEffectSystem>()?.AddShake( WorldPosition, HeavyShakeRadius, MathF.Min( HeavyShakeAmplitude, 16 ), HeavyShakeFrequency, HeavyShakeDuration );
	}

	// Death

	/// <summary>It doesn't fall at once: it plays its death clip, untouchable, and only drops as a ragdoll at the clip's end.</summary>
	void Component.IDamageable.IEvents.OnDying( Component.IDamageable.DyingEvent e )
	{
		if ( e.Victim != GameObject || _finishing || _death is null || !Networking.IsHost )
			return;

		e.Cancelled = true;
		if ( Busy == Doing.Dying )
			return;

		_deathInfo = e.DamageInfo;
		GetComponent<Charge>()?.Interrupt();
		End( false );
		Npc.Movement?.Stop();
		Begin( Doing.Dying, _death );
		PlaySound( "NPC_AntlionGuard.Die" );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || Busy != Doing.Dying || _finishing || _actionTime < _deathEvent * Duration( _death, 2.87f ) )
			return;

		// The killing blow lands again, now that it can fall
		_finishing = true;
		var last = _deathInfo;
		var info = new DamageInfo( Npc.Health + 1000, last?.Attacker, last?.Weapon ) { Position = last?.Position ?? WorldPosition, Origin = last?.Origin ?? WorldPosition };
		if ( last is not null )
			foreach ( var tag in last.Tags.TryGetAll() )
				info.Tags.Add( tag );
		((Component.IDamageable)Npc).OnDamage( info );
	}

	void INpcPart.OnNpcDeath( Component.IDamageable.DeathEvent death )
	{
		PlaySound( "NPC_AntlionGuard.Fallover" );
		if ( _prop.IsValid() )
			Claimed.Remove( _prop );
	}
}
