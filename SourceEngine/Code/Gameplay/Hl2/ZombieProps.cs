using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using Sandbox.Volumes;

namespace Source1.Hl2;

/// <summary>
/// A zombie and the physics props around it: it swats a light resting prop at its enemy, steps onto low props and round
/// others in its path, claws one it cannot get round, and otherwise waits a moment, then looks for another way. The
/// navmesh does not see props, so the route ahead is swept with the zombie's hull.
/// </summary>
[Title( "HL2 Zombie Props" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2ZombieProps", "Hl2ZombieProps" )]
public sealed class ZombieProps : CreatureBehaviour
{
	/// <summary>The wind-up vocal of a swat, such as Zombie.Attack; empty for none.</summary>
	[Property] public string AttackSound { get; set; }

	/// <summary>Only swats props its enemy can see, so it is not pelted from round a corner.</summary>
	[Property] public bool EnemyMustSeeProp { get; set; }

	const float MaxSwatMass = 60;
	const float MaxClawMass = 100;
	const float SearchRadius = 240;
	const float MaxEnemyDistance = 1000;
	const int MaxExamined = 100;

	/// <summary>Flattened dot product between the directions to the enemy and to the prop.</summary>
	const float TowardsEnemy = 0.8f;

	/// <summary>Block distance within which it swats from where it stands; farther, it walks to the prop first.</summary>
	const float SwatReach = 80;
	const float SwatSpeed = 800;
	const float SwatLift = 200;

	const float ScanInterval = 2;
	const float ScanAfterSwat = 5;
	const float AttemptInterval = 3;

	/// <summary>Push on the blocker when the claw sweep missed it.</summary>
	const float ClawFallbackPush = 120;

	/// <summary>Props lower than this are walked over; this close ahead counts as up against one.</summary>
	const float StepHeight = 18;
	const float ContactGap = 10;

	/// <summary>Degrees per second turning on the spot.</summary>
	const float TurnRate = 180;
	const float DetourMargin = 8;

	/// <summary>Seconds it stands at a prop before looking for another way, and how long that spot stays off its routes.</summary>
	const float WaitLimit = 1;
	const float AvoidTime = 4;
	/// <summary>Map props flagged as debris, which characters walk through.</summary>
	public const string DebrisTag = "map_debris";

	static readonly string[] PassThrough = ["trigger", "debris", DebrisTag, "ragdoll", "npc", "player", "playercontroller", "projectile"];

	enum Step { None, Approach, Turn, Play, Detour, Wander }

	[Sync( SyncFlags.FromHost )] string Playing { get; set; }

	/// <summary>How high it stands on a low prop; the agent stays on the navmesh, so the model and collider are raised.</summary>
	[Sync( SyncFlags.FromHost )] float Lift { get; set; }
	float _shownLift;

	string _rightMid, _rightLow, _leftMid, _leftLow;
	string[] _claws = [];
	Step _step;
	TimeSince _stepTime;
	TimeSince _lastThink;
	GameObject _enemy;
	Rigidbody _prop;
	TimeUntil _nextAttempt;
	TimeUntil _nextScan;
	bool _claw;
	(float Time, Action<GameObject> Fire)[] _cues = [];
	int _cue;
	float _clipDuration;
	TimeSince _lastPath;
	readonly List<Vector3> _waypoints = [];
	Vector3 _progressFrom;
	TimeSince _progressTime;
	TimeUntil _nextDetour;
	Step _resume;
	float _waited;
	GameObject _avoided;
	Rigidbody _avoidedProp;
	Rigidbody _cornered;
	Vector3 _avoidedFrom;
	Vector3 _wanderGoal;
	TimeSince _sinceAvoided;

	public override string Animation => Playing;

	public override IEnumerable<(string Name, bool Loop)> AnimationChoices =>
		[(_rightMid, false), (_rightLow, false), (_leftMid, false), (_leftLow, false), .. _claws.Select( x => (x, false) )];

	/// <summary>A model without all four swat clips never swats; it still claws props in its way.</summary>
	bool CanSwat => _rightMid is not null && _rightLow is not null && _leftMid is not null && _leftLow is not null;

	NavMeshAgent Agent => GetComponent<NavMeshAgent>();
	float HullRadius => Agent?.Radius ?? 13;
	float HullHeight => Agent?.Height ?? 72;
	Vector3 Center => WorldPosition + Vector3.Up * (HullHeight * 0.5f);

	protected override void OnConfigure( Model model, ModelSequences sequences )
	{
		_rightMid = sequences.FindActivity( "ACT_ZOM_SWATRIGHTMID" );
		_rightLow = sequences.FindActivity( "ACT_ZOM_SWATRIGHTLOW" );
		_leftMid = sequences.FindActivity( "ACT_ZOM_SWATLEFTMID" );
		_leftLow = sequences.FindActivity( "ACT_ZOM_SWATLEFTLOW" );
		_claws = sequences.Clips( "ACT_MELEE_ATTACK1" );
	}

	protected override void OnUpdate()
	{
		// At once, so its collider never sinks into the prop.
		if ( Networking.IsHost && Npc.IsValid() )
			Lift = Footing();

		if ( _shownLift == Lift )
			return;

		foreach ( var child in GameObject.Children.Where( c => c == Npc?.Renderer?.GameObject || c.GetComponent<Collider>().IsValid() ) )
			child.LocalPosition += Vector3.Up * (Lift - _shownLift);
		_shownLift = Lift;
	}

	/// <summary>The top of a prop under its feet within step height, measured from the floor.</summary>
	float Footing()
	{
		var feet = new BBox( new Vector3( -HullRadius, -HullRadius, 0 ), new Vector3( HullRadius, HullRadius, 0 ) );
		var hit = Scene.Trace.Box( feet, WorldPosition + Vector3.Up * StepHeight, WorldPosition )
			.IgnoreStatic().IgnoreGameObjectHierarchy( GameObject ).WithoutTags( PassThrough ).Run();
		return hit.Hit && !hit.StartedSolid && IsLooseProp( hit.GameObject?.GetComponentInParent<Rigidbody>() ) ? hit.EndPosition.z - WorldPosition.z : 0;
	}

	/// <summary>Host think; true while a swat, a claw or a detour owns the zombie.</summary>
	public override bool Think( GameObject target )
	{
		if ( !Networking.IsHost || !Npc.IsValid() )
			return false;

		// Another behaviour took over mid-step; what was under way no longer fits.
		if ( _lastThink > 0.25f && _step != Step.None )
			Finish();
		_lastThink = 0;

		if ( _prop is not null && !_prop.IsValid() )
			_prop = null;

		switch ( _step )
		{
			case Step.Approach: return Approach( target );
			case Step.Turn: return Turn( target );
			case Step.Play: return Play( target );
			case Step.Detour: return FollowDetour();
			case Step.Wander: return Wander( target );
		}

		// The blocked spot is off its routes and it still hasn't got anywhere: there's no other way. A light prop is
		// clawed through, the spot back on its routes; otherwise it wanders off.
		if ( _avoided.IsValid() && _sinceAvoided > 1.5f && (WorldPosition - _avoidedFrom).WithZ( 0 ).Length < 12 && target.IsValid() )
		{
			_avoidedFrom = WorldPosition;
			_sinceAvoided = 0;
			if ( _claws.Length > 0 && _avoidedProp.IsValid() && _avoidedProp.Mass < MaxClawMass )
			{
				_cornered = _avoidedProp;
				_avoided.Destroy();
			}
			else if ( RandomSpot( 48, 240 ) is { } goal )
			{
				_wanderGoal = goal;
				_lastPath = 10;
				return Begin( Step.Wander );
			}
		}

		if ( target.IsValid() && CanSwat && _prop is null && _nextScan )
			_prop = FindProp( target );

		if ( target.IsValid() && CanSwat && _prop is not null && _nextAttempt && Visible( Npc.EyePosition, EyeOf( target ), GameObject, target.Root ) )
		{
			_nextAttempt = AttemptInterval;
			_enemy = target;
			_lastPath = 10;
			return Begin( BlockDistance( WorldPosition, _prop.GetWorldBounds().Center ) <= SwatReach ? Step.Turn : Step.Approach );
		}

		return AvoidProps( target );
	}

	bool Begin( Step step )
	{
		Npc.Movement?.Stop();
		_step = step;
		_stepTime = 0;
		_waited = 0;
		return true;
	}

	bool Finish()
	{
		_prop = _claw ? null : _prop; // A clawed blocker is no prop to swat.
		_step = Step.None;
		_enemy = null;
		_claw = false;
		_waypoints.Clear();
		Playing = null;
		return false;
	}

	/// <summary>A swat or claw is dropped when its enemy dies or another enemy takes over.</summary>
	bool Abandoned( GameObject target ) => !target.IsValid() || target != _enemy || Npcs.HealthOf( target ) is { Health: <= 0 };

	/// <summary>The nearest light, resting prop towards the enemy and nearer the zombie than the enemy, that it can see.</summary>
	Rigidbody FindProp( GameObject target )
	{
		_nextScan = ScanInterval;
		var origin = WorldPosition;
		var enemy = target.WorldPosition;
		if ( origin.Distance( enemy ) > MaxEnemyDistance )
			return null;

		var radius = MathF.Min( origin.Distance( enemy ), SearchRadius );
		var box = BBox.FromPositionAndSize( origin, new Vector3( radius * 2, radius * 2, HullHeight * 4 ) );
		var toEnemy = (enemy - origin).Normal;
		var examined = new HashSet<Rigidbody>();
		Rigidbody best = null;
		var bestDistance = radius;

		foreach ( var go in Scene.FindInPhysics( box ) )
		{
			var body = go.GetComponentInParent<Rigidbody>();
			if ( !body.IsValid() || !examined.Add( body ) )
				continue;
			if ( examined.Count > MaxExamined )
				break;

			if ( !IsLooseProp( body ) || body.GetComponentInParent<ModelPhysics>().IsValid() || !body.MotionEnabled || !body.Sleeping || body.Mass > MaxSwatMass )
				continue;

			var bounds = body.GetWorldBounds();
			var center = bounds.Center;
			var distance = BlockDistance( origin, center );
			// Flattened without renormalising, so a height difference narrows the cone.
			var toProp = (center - origin).Normal;
			if ( distance >= bestDistance || distance >= BlockDistance( center, enemy ) || toEnemy.x * toProp.x + toEnemy.y * toProp.y < TowardsEnemy )
				continue;

			// Tall enough to reach its knees, no higher than its eyes.
			if ( bounds.Maxs.z < origin.z + HullHeight * 0.25f || center.z > Npc.EyePosition.z )
				continue;

			if ( !Visible( Npc.EyePosition, body.WorldPosition, GameObject, body.GameObject ) )
				continue;
			if ( EnemyMustSeeProp && !Visible( EyeOf( target ), body.WorldPosition, target, body.GameObject ) )
				continue;

			best = body;
			bestDistance = distance;
		}

		return best;
	}

	/// <summary>Prop distances are flat and measured along the axes.</summary>
	static float BlockDistance( Vector3 a, Vector3 b ) => MathF.Abs( a.x - b.x ) + MathF.Abs( a.y - b.y );

	/// <summary>A physics object that is not a character, corpse, gib, weapon, projectile or debris.</summary>
	bool IsLooseProp( Rigidbody body )
	{
		if ( !body.IsValid() || body.GameObject == GameObject || body.PhysicsBody is not { IsValid: true } )
			return false;

		var tags = body.GameObject.Tags;
		return !tags.Has( "debris" ) && !tags.Has( "ragdoll" ) && !tags.Has( "npc" ) && !tags.Has( "player" ) && !tags.Has( "projectile" )
			&& !body.GetComponentInParent<BaseNpc>().IsValid() && !body.GetComponentInParent<PlayerController>().IsValid()
			&& !body.GetComponentInParent<Gib>().IsValid() && !body.GetComponentInParent<BaseCombatWeapon>().IsValid();
	}

	/// <summary>A clear line between two points, or one that first meets the object looked at.</summary>
	bool Visible( Vector3 from, Vector3 to, GameObject looker, GameObject seen )
	{
		var trace = Scene.Trace.Ray( from, to ).IgnoreGameObjectHierarchy( looker ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "debris" ).Run();
		return !trace.Hit || trace.GameObject.IsValid() && (trace.GameObject == seen || seen.IsDescendant( trace.GameObject ));
	}

	static Vector3 EyeOf( GameObject target ) => target.GetComponent<PlayerController>() is { } player && player.IsValid() ? player.EyeTransform.Position
		: target.GetComponent<BaseNpc>() is { } npc && npc.IsValid() ? npc.EyePosition
		: Npcs.CapturePoint( target );

	/// <summary>Walks to the prop, held to a walk, until close enough to swat it or pressed against it.</summary>
	bool Approach( GameObject target )
	{
		if ( Abandoned( target ) || !_prop.IsValid() || Npc.Movement is null )
			return Finish();

		// Close enough that the block distance is within reach from any side.
		var arrive = MathF.Min( HullRadius * 4, SwatReach / MathF.Sqrt( 2 ) );
		var center = _prop.GetWorldBounds().Center;
		if ( (center - WorldPosition).WithZ( 0 ).Length <= arrive )
			return Begin( Step.Turn );

		if ( WalkTo( Npc.Movement.ProjectPoint( center ) ?? center, arrive ) is { } arrived )
			return arrived ? Begin( Step.Turn ) : Finish();

		if ( Probe() is not { } blocked )
			return true;
		if ( blocked.Body == _prop )
			return blocked.Free > ContactGap || Begin( Step.Turn );

		// Steps round another prop in the way and walks on. With no way round the walk fails: a light blocker is clawed.
		if ( _nextDetour && TryDetour( blocked ) || blocked.Free > ContactGap )
			return true;
		if ( _claws.Length > 0 && blocked.Body.Mass < MaxClawMass )
			return Claw( blocked, target );

		Npc.Movement.Stop();
		return Finish();
	}

	/// <summary>Turns on the spot to face the enemy, then claws or swats.</summary>
	bool Turn( GameObject target )
	{
		if ( Abandoned( target ) )
			return Finish();

		var flat = (target.WorldPosition - WorldPosition).WithZ( 0 );
		var yaw = WorldRotation.Yaw();
		var delta = flat.Length < 1 ? 0 : Angles.NormalizeAngle( Rotation.LookAt( flat ).Yaw() - yaw );
		var step = TurnRate * Time.Delta;
		WorldRotation = Rotation.FromYaw( yaw + Math.Clamp( delta, -step, step ) );
		if ( MathF.Abs( delta ) > MathF.Max( step, 1 ) && _stepTime < 2 )
			return true;

		if ( _claw )
			return BeginClaw();

		if ( !_prop.IsValid() || BlockDistance( WorldPosition, _prop.GetWorldBounds().Center ) > SwatReach )
			return Finish();

		// The swing by the prop's side and height, judged now that it faces the enemy.
		var right = (_prop.WorldPosition - WorldPosition).WithZ( 0 ).Dot( WorldRotation.Right ) >= 0;
		var high = _prop.GetWorldBounds().Center.z > Center.z;
		var clip = right ? high ? _rightMid : _rightLow : high ? _leftMid : _leftLow;
		var events = ModelSequences.Of( ConfiguredModel ).Get( clip )?.Events ?? [];
		float Cue( string name, float fallback ) => Math.Clamp( events.FirstOrDefault( e => e.Name == name )?.Cycle ?? fallback, 0, 1 ) * Duration( clip, 1 );
		return BeginClip( clip, [(Cue( "AE_ZOMBIE_STARTSWAT", 0.2f ), WindUp), (Cue( "AE_ZOMBIE_SWATITEM", 0.5f ), Launch)] );
	}

	bool BeginClaw()
	{
		var clip = Game.Random.FromArray( _claws );
		var hits = Npcs.AttackEvents( ConfiguredModel, clip, Npc.AttackDelay );
		return BeginClip( clip, hits.Select( x => (x.Time, (Action<GameObject>)ClawHit) ).ToArray() );
	}

	bool BeginClip( string clip, (float, Action<GameObject>)[] cues )
	{
		_cues = cues;
		_cue = 0;
		_clipDuration = Duration( clip, 1 );
		Playing = clip;
		return Begin( Step.Play );
	}

	/// <summary>Plays the swat or claw, firing its animation events as their times come.</summary>
	bool Play( GameObject target )
	{
		if ( Abandoned( target ) )
			return Finish();

		while ( _cue < _cues.Length && _stepTime >= _cues[_cue].Time )
			_cues[_cue++].Fire( target );

		return _stepTime < _clipDuration || Finish();
	}

	void WindUp( GameObject target )
	{
		if ( !string.IsNullOrEmpty( AttackSound ) )
			PlaySound( AttackSound );
	}

	/// <summary>Throws the prop at the enemy's middle, the same speed whatever it weighs, with a lift and a tumble.</summary>
	void Launch( GameObject target )
	{
		if ( !_prop.IsValid() )
			return;

		var aim = Npcs.CapturePoint( target );
		PlaySound( "NPC_BaseZombie.Swat" );
		PlayImpact( _prop.GameObject, aim );

		_prop.Sleeping = false;
		_prop.Velocity += (aim - _prop.GetWorldBounds().Center).Normal * SwatSpeed + Vector3.Up * SwatLift;
		var spin = new Vector3( Game.Random.Float( -180, 180 ), 20, Game.Random.Float( -360, 360 ) ) * (MathF.PI / 180);
		_prop.AngularVelocity += _prop.WorldRotation * spin;
		_prop = null;
		_nextScan = ScanAfterSwat;
	}

	/// <summary>The prop's own hard impact, at half volume, heard at the enemy it is about to hit.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayImpact( GameObject prop, Vector3 position )
	{
		var physics = prop.IsValid() ? prop.GetComponent<Rigidbody>()?.PhysicsBody : null;
		var surface = physics is { IsValid: true } ? physics.Surface ?? physics.Shapes.FirstOrDefault()?.Surface : null;
		var sound = surface?.SoundCollection.ImpactHard ?? surface?.GetBaseSurface()?.SoundCollection.ImpactHard;
		if ( Application.IsDedicatedServer || !sound.IsValid() )
			return;

		var handle = Sound.Play( sound, position );
		if ( handle.IsValid() )
			handle.Volume *= 0.5f;
	}

	/// <summary>
	/// One claw, the zombie's shared sweep ahead: props on the way and the first character it hates take it. The chosen
	/// prop takes it wherever it is when the sweep meets nothing. Solid world between the zombie and its enemy stops the whole claw.
	/// </summary>
	void ClawHit( GameObject target )
	{
		var center = Center;
		if ( Scene.Trace.Box( BBox.FromPositionAndSize( Vector3.Zero, 16 ), center, Npcs.CapturePoint( target ) )
			.IgnoreDynamic().IgnoreGameObjectHierarchy( GameObject ).WithoutTags( PassThrough ).Run().Hit )
			return;

		var hit = Npc.SweepHit( center, center + WorldRotation.Forward * Npc.AttackRange, HullRadius * 2, null, anyone: false );
		if ( hit.Prop.IsValid() && hit.Prop == _prop?.GameObject )
			Clawed();
		else if ( !hit.Character.IsValid() && !hit.Prop.IsValid() )
		{
			if ( !_prop.IsValid() )
			{
				PlaySound( "Zombie.AttackMiss" );
				return;
			}

			var bodyCenter = _prop.GetWorldBounds().Center;
			Npc.Damage( _prop.GameObject, center, bodyCenter, DamageTags.Melee );
			if ( _prop.IsValid() && _prop.MotionEnabled )
				_prop.ApplyImpulseAt( center, (bodyCenter - center).Normal * ClawFallbackPush );
			Clawed();
		}

		PlaySound( "Zombie.AttackHit" );
	}

	/// <summary>A clawed prop is no longer the one it means to swat; it tries again a little later.</summary>
	void Clawed()
	{
		_prop = null;
		_nextAttempt = Game.Random.Float( 2, 4 );
	}

	/// <summary>The first prop the hull would walk into along the route ahead, and the room left before it.</summary>
	readonly record struct Blocked( Rigidbody Body, float Free, Vector3 Direction );

	SceneTrace Hull( Vector3 from, Vector3 to )
	{
		var radius = HullRadius;
		var box = new BBox( new Vector3( -radius, -radius, StepHeight + Lift ), new Vector3( radius, radius, MathF.Max( HullHeight, StepHeight + 1 ) + Lift ) );
		return Scene.Trace.Box( box, from, to ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( PassThrough );
	}

	bool Clear( Vector3 from, Vector3 to ) => !Hull( from, to ).Run().Hit;

	Blocked? Probe()
	{
		var agent = Agent;
		if ( agent is null || !agent.IsNavigating || Npc.Movement?.MoveTarget is null )
			return null;

		var reach = HullRadius * 2 + MathF.Max( agent.Velocity.WithZ( 0 ).Length * 0.3f, ContactGap );
		var direction = (agent.GetLookAhead( reach ) - WorldPosition).WithZ( 0 );
		if ( direction.Length < 1 )
			return null;

		direction = direction.Normal;
		foreach ( var hit in Hull( WorldPosition, WorldPosition + direction * reach ).RunAll().OrderBy( x => x.Fraction ) )
		{
			var body = hit.GameObject.IsValid() ? hit.GameObject.GetComponentInParent<Rigidbody>() : null;
			// A prop it already stands in, such as one that landed on it, lets it walk out.
			if ( hit.Hit && IsLooseProp( body ) && !(hit.StartedSolid && (WorldPosition - body.GetWorldBounds().Center).Dot( direction ) > 0) )
				return new Blocked( body, hit.StartedSolid ? 0 : hit.Fraction * reach, direction );
		}

		return null;
	}

	/// <summary>
	/// Ordinary movement: step round a prop in the way, else stand a moment and plan another way round. A light prop is
	/// clawed when no other way would help: the route runs straight past it to its end, or re-planning found none.
	/// </summary>
	bool AvoidProps( GameObject target )
	{
		var waited = _waited;
		_waited = 0;
		if ( Probe() is not { } blocked )
			return false;

		// Its chosen prop is no obstacle: it walks on up to it and waits there for the swat, dropping the choice if none comes.
		var chosen = blocked.Body == _prop;
		if ( chosen && blocked.Free > ContactGap )
			return false;
		_prop = chosen && waited > AttemptInterval ? null : _prop;

		if ( !chosen && _nextDetour && TryDetour( blocked ) )
			return true;

		// Walks on up to it, then turns to the enemy and claws; while the claw isn't ready it waits for it.
		var claw = !chosen && target.IsValid() && _claws.Length > 0 && blocked.Body.Mass < MaxClawMass && (blocked.Body == _cornered || LastLeg());
		if ( claw && _nextAttempt )
			return blocked.Free <= ContactGap && Claw( blocked, target );

		_waited = waited + Time.Delta;
		if ( _waited > WaitLimit && !claw && !chosen )
			AvoidSpot( blocked.Body );
		(Npc.Movement as NavigationLayer)?.HoldMovement( 0.25f );
		return false;
	}

	/// <summary>The route runs straight on to its end, so there's no other way to take.</summary>
	bool LastLeg() => Npc.Movement?.MoveTarget is { } goal
		&& (Agent.GetLookAhead( (goal - WorldPosition).WithZ( 0 ).Length ) - goal).WithZ( 0 ).Length < 24;

	/// <summary>A short walk anywhere, when there's no way on to its enemy; props in its way are dealt with as in the chase.</summary>
	bool Wander( GameObject target )
	{
		if ( Npc.Movement is null || _stepTime > 10 || WalkTo( _wanderGoal, 4 ) is not null )
			return Finish();

		AvoidProps( target );
		return true;
	}

	/// <summary>Walks at a walk, re-planning now and then: whether it got there once the move ends, null while under way.</summary>
	bool? WalkTo( Vector3 goal, float stop )
	{
		if ( _lastPath > 0.5f )
		{
			_lastPath = 0;
			Npc.Movement.MoveTo( goal, stop, Npc.PaceFor( false ), null );
			return null;
		}

		return Npc.Movement.Status switch { MovementLayer.MovementStatus.Arrived => true, MovementLayer.MovementStatus.Failed => false, _ => null };
	}

	bool Claw( Blocked blocked, GameObject target )
	{
		_prop = blocked.Body;
		_enemy = target;
		_claw = true;
		return Begin( Step.Turn );
	}

	/// <summary>Takes the prop's spot off the navmesh for a few seconds, so the chase plans another way round.</summary>
	void AvoidSpot( Rigidbody body )
	{
		if ( _avoided.IsValid() )
			return;

		var bounds = body.GetWorldBounds();
		var go = new GameObject( false, "Avoided prop" ) { WorldPosition = bounds.Center, NetworkMode = NetworkMode.Never };
		var area = go.AddComponent<NavMeshArea>();
		area.IsBlocker = true;
		area.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Box, Box = BBox.FromPositionAndSize( Vector3.Zero, bounds.Size + new Vector3( HullRadius ) ) };
		go.Enabled = true;
		area.Invoke( AvoidTime, area.DestroyGameObject );
		_avoided = go;
		_avoidedProp = body;
		_avoidedFrom = WorldPosition;
		_sinceAvoided = 0;
	}

	/// <summary>A way round the nearer side of the prop: a sidestep, a straight walk past, then back to the route. Square legs, as a diagonal cuts its corners.</summary>
	bool TryDetour( Blocked blocked )
	{
		_nextDetour = 0.5f;
		var origin = WorldPosition;
		var forward = blocked.Direction;
		var side = forward.Cross( Vector3.Up ).Normal;
		var bounds = blocked.Body.GetWorldBounds();
		var corners = new[] { bounds.Mins, bounds.Maxs, new Vector3( bounds.Mins.x, bounds.Maxs.y, 0 ), new Vector3( bounds.Maxs.x, bounds.Mins.y, 0 ) }
			.Select( c => (c - origin).WithZ( 0 ) ).ToArray();
		var margin = HullRadius + DetourMargin;
		var past = corners.Max( c => c.Dot( forward ) ) + margin;

		if ( OnNavmesh( Agent.GetLookAhead( past + HullRadius * 2 ) ) is not { } rejoin )
			return false;

		foreach ( var lateral in new[] { corners.Max( c => c.Dot( side ) ) + margin, corners.Min( c => c.Dot( side ) ) - margin }.OrderBy( MathF.Abs ) )
		{
			if ( OnNavmesh( origin + side * lateral ) is not { } aside || OnNavmesh( origin + forward * past + side * lateral ) is not { } beyond
				|| !Clear( origin, aside ) || !Clear( aside, beyond ) || !Clear( beyond, rejoin ) )
				continue;

			// Walks the legs, then goes back to the walk it left, or to the chase at the chase's pace.
			_waypoints.Clear();
			_waypoints.AddRange( [aside, beyond, rejoin] );
			_progressFrom = origin;
			_progressTime = 0;
			_resume = _step;
			_step = Step.Detour;
			Npc.Movement.MoveTo( aside, 2, Npc.PaceFor( _resume == Step.None ), null );
			return true;
		}

		return false;
	}

	/// <summary>The nearest navmesh point to a spot, if there is one close by.</summary>
	Vector3? OnNavmesh( Vector3 point ) => Npc.Movement?.ProjectPoint( point ) is { } found && (found - point).WithZ( 0 ).Length < 8
		&& MathF.Abs( found.z - point.z ) < StepHeight * 2 ? found : null;

	/// <summary>Follows the detour; one that covers under 12 units in a second is given up.</summary>
	bool FollowDetour()
	{
		// Close to each corner, so the next leg keeps its clearance from the prop.
		if ( (_waypoints[0] - WorldPosition).WithZ( 0 ).Length < 4 )
		{
			_waypoints.RemoveAt( 0 );
			_lastPath = 10;
			if ( _waypoints.Count == 0 )
				return _resume == Step.None ? Finish() : Begin( _resume );

			Npc.Movement?.MoveTo( _waypoints[0], 2, Npc.PaceFor( _resume == Step.None ), null );
		}

		if ( Npc.Movement?.Status == MovementLayer.MovementStatus.Failed )
			return Finish();
		if ( _progressTime < 1 )
			return true;
		if ( (WorldPosition - _progressFrom).WithZ( 0 ).Length < 12 )
		{
			_nextDetour = 3;
			Npc.Movement?.Stop();
			return Finish();
		}

		_progressTime = 0;
		_progressFrom = WorldPosition;
		return true;
	}
}
