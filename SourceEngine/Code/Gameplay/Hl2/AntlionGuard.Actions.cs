using System;
using Sandbox.Navigation;
using MoveStatus = Sandbox.Npcs.Layers.MovementLayer.MovementStatus;

namespace Source1.Hl2;

/// <summary>
/// The antlion guard's own actions besides the shove and the charge: choosing what to do, what it does when it can't reach
/// its enemy (a frustrated roar, cover, a run toward the enemy, a wander), its full-body flinch at heavy hits and its
/// limping run when badly hurt. Hitting props, damage and death have their own parts.
/// </summary>
public sealed partial class AntlionGuard
{
	/// <summary>What the guard is busy with, besides chasing and shoving.</summary>
	public enum Doing { None, Flinch, Approach, Turn, Swat, Roar, Cover, Seek, Wander, Wait, Dying }

	/// <summary>Seconds after a full-body flinch before heavy damage flinches it again.</summary>
	const float FlinchCooldown = 8;

	// Out of reach
	const float SeekBeyond = 1200;
	const float WanderRadius = 1024;
	const float RecheckMove = 240;
	/// <summary>Seconds before a spot it found out of reach is tried again anyway.</summary>
	const float RecheckTime = 3;

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
	bool Hurt => Npc.IsValid() && Npc.Health >= 1 && Npc.MaxHealth > 0 && Npc.Health <= Npc.MaxHealth * BadlyHurt;

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

	/// <summary>Nothing of its own going on: once a second it looks for a prop to hit, and deals with an enemy it can't reach.</summary>
	bool Choose( GameObject enemy )
	{
		if ( Npcs.IsDead( enemy ) )
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

		Npcs.TurnToward( GameObject, YawTo( _enemy.WorldPosition ), StandingTurnRate );
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
	float StandingTurnRate => Nav?.TurnRateAgainst( _enemy ) ?? 200;

	/// <summary>Squares up to its enemy, then roars without turning.</summary>
	bool Roaring()
	{
		if ( Playing is null )
		{
			var left = _enemy.IsValid() ? Npcs.TurnToward( GameObject, YawTo( _enemy.WorldPosition ), StandingTurnRate ) : 0;
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

	/// <summary>A clip's ground motion and turn from its start to a time; it plays once, so its motion ends with it.</summary>
	(Vector3 Position, float Yaw) Travelled( string clip, float time ) =>
		ModelSequences.Of( ConfiguredModel ).Travelled( clip, Duration( clip, 0 ), time, loop: false );

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
}
